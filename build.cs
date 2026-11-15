#!/usr/bin/env dotnet

using System.Buffers.Binary;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

string[] defaultRids = ["win-x64", "win-arm64", "osx-arm64"];
List<string> rids = [];
string configuration = "Release";
string outputRoot = "artifacts";
string? version = null;
bool skipTests = false;
bool selfContained = true;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rid" or "-r":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine($"Error: {args[i]} requires a runtime identifier value.");
                return 1;
            }
            foreach (var r in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                rids.Add(r);
            }
            break;
        case "--configuration" or "-c":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine($"Error: {args[i]} requires a configuration name.");
                return 1;
            }
            configuration = args[++i];
            break;
        case "--output" or "-o":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine($"Error: {args[i]} requires an output directory path.");
                return 1;
            }
            outputRoot = args[++i];
            break;
        case "--version" or "-v":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine($"Error: {args[i]} requires a version value.");
                return 1;
            }
            version = args[++i].Trim().TrimStart('v', 'V');
            if (string.IsNullOrWhiteSpace(version))
            {
                Console.Error.WriteLine("Error: --version value cannot be empty.");
                return 1;
            }
            break;
        case "--skip-tests":
            skipTests = true;
            break;
        case "--no-self-contained" or "--framework-dependent":
            selfContained = false;
            break;
        case "--help" or "-h":
            PrintHelp();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown option: {args[i]}");
            PrintHelp();
            return 1;
    }
}

if (rids.Count == 0)
{
    rids.AddRange(defaultRids);
}

string repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
string artifactsDir = Path.GetFullPath(Path.Combine(repoRoot, outputRoot));
EnsureUnderDirectory(artifactsDir, repoRoot, "output directory");
string publishBaseDir = Path.Combine(artifactsDir, "publish");
string packagesDir = Path.Combine(artifactsDir, "packages");

Directory.CreateDirectory(publishBaseDir);
Directory.CreateDirectory(packagesDir);

Console.WriteLine($"Repository Root : {repoRoot}");
Console.WriteLine($"Configuration   : {configuration}");
if (version is not null)
{
    Console.WriteLine($"Version         : {version}");
}
Console.WriteLine($"Self-Contained  : {selfContained}");
Console.WriteLine($"Target RIDs     : {string.Join(", ", rids)}");
Console.WriteLine($"Output Directory: {packagesDir}");
Console.WriteLine();

if (!skipTests)
{
    Console.WriteLine("=== Running Unit Tests ===");
    RunDotnet(repoRoot, "test", "Vectorer.slnx", "-c", configuration, "-m:1", "--disable-build-servers");
    Console.WriteLine();
}

string appProject = Path.Combine(repoRoot, "src", "Vectorer.App", "Vectorer.App.csproj");

foreach (var rid in rids)
{
    Console.WriteLine($"=== Publishing Vectorer for {rid} ===");
    string ridPublishDir = Path.GetFullPath(Path.Combine(publishBaseDir, rid));
    EnsureUnderDirectory(ridPublishDir, publishBaseDir, "publish directory");
    if (Directory.Exists(ridPublishDir))
    {
        Directory.Delete(ridPublishDir, recursive: true);
    }
    Directory.CreateDirectory(ridPublishDir);

    List<string> publishArgs =
    [
        "publish",
        appProject,
        "-c", configuration,
        "-r", rid,
        selfContained ? "--self-contained" : "--no-self-contained",
        "-o", ridPublishDir,
        "-m:1",
        "--disable-build-servers",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    ];

    if (version is not null)
    {
        publishArgs.Add($"-p:Version={version}");
    }

    RunDotnet(repoRoot, [.. publishArgs]);
    VerifyLocalizationResources(ridPublishDir, rid);

    CopyDocIfPresent(repoRoot, ridPublishDir, "LICENSE");
    CopyDocIfPresent(repoRoot, ridPublishDir, "THIRD_PARTY_NOTICES.md");
    CopyDocIfPresent(repoRoot, ridPublishDir, "README.md");
    CopyDocIfPresent(repoRoot, ridPublishDir, "README.en.md");

    Directory.CreateDirectory(Path.Combine(ridPublishDir, "plugins"));

    if (rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
    {
        string archivePath = Path.Combine(packagesDir, $"Vectorer-{rid}.zip");
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        Console.WriteLine($"Creating ZIP archive: {archivePath}");
        ZipFile.CreateFromDirectory(ridPublishDir, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        VerifyArchiveLocalizationResources(archivePath, rid, isZip: true);
    }
    else
    {
        string archivePath = Path.Combine(packagesDir, $"Vectorer-{rid}.tar.gz");
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        Console.WriteLine($"Creating tar.gz archive: {archivePath}");
        CreateTarGzArchive(ridPublishDir, archivePath, $"Vectorer-{rid}");
        VerifyArchiveLocalizationResources(archivePath, rid, isZip: false);
    }

    Console.WriteLine($"Successfully packaged {rid}!");
    Console.WriteLine();
}

Console.WriteLine("=== Build & Packaging Complete ===");
foreach (var file in Directory.GetFiles(packagesDir))
{
    var info = new FileInfo(file);
    Console.WriteLine($"  - {info.Name} ({info.Length / (1024.0 * 1024.0):F2} MB)");
}

return 0;

static void PrintHelp()
{
    Console.WriteLine("""
        Vectorer Build & Package Script (.NET 10 File-based App)

        Usage:
          dotnet run build.cs -- [options]

        Options:
          -r, --rid <RID>           Target Runtime Identifier(s), comma-separated or specified multiple times.
                                    Default: win-x64, win-arm64, osx-arm64
          -c, --configuration <CFG> Build configuration (Default: Release)
          -o, --output <DIR>        Artifacts output root directory (Default: artifacts)
          -v, --version <VER>       Package/assembly version passed to dotnet publish (leading 'v' is trimmed)
          --skip-tests              Skip running unit tests before publishing
          --no-self-contained       Publish as framework-dependent instead of self-contained
          -h, --help                Show this help message
        """);
}

static string FindRepoRoot(string startDir)
{
    var dir = new DirectoryInfo(startDir);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Vectorer.slnx")))
        {
            return dir.FullName;
        }
        dir = dir.Parent;
    }
    return startDir;
}

static void CopyDocIfPresent(string repoRoot, string destinationDir, string fileName)
{
    string sourcePath = Path.Combine(repoRoot, fileName);
    if (File.Exists(sourcePath))
    {
        File.Copy(sourcePath, Path.Combine(destinationDir, fileName), overwrite: true);
    }
}

static void RunDotnet(string workingDirectory, params string[] arguments)
{
    var psi = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = workingDirectory,
        UseShellExecute = false
    };

    foreach (var arg in arguments)
    {
        psi.ArgumentList.Add(arg);
    }

    // In restricted sandbox environments where NuGet global-packages fallback is needed:
    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string localNugetCache = Path.Combine(userProfile, ".nuget", "packages");

    Console.WriteLine($"> dotnet {string.Join(" ", arguments)}");
    using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet process.");
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Command 'dotnet {string.Join(" ", arguments)}' failed with exit code {process.ExitCode}.");
    }
}

static void CreateTarGzArchive(string sourceDirectory, string archivePath, string rootFolderName)
{
    using var fileStream = File.Create(archivePath);
    using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);
    using var tarWriter = new TarWriter(gzipStream, TarEntryFormat.Pax);

    var rootDirInfo = new DirectoryInfo(sourceDirectory);

    foreach (var dir in rootDirInfo.EnumerateDirectories("*", SearchOption.AllDirectories))
    {
        string relPath = Path.GetRelativePath(sourceDirectory, dir.FullName).Replace('\\', '/');
        var dirEntry = new PaxTarEntry(TarEntryType.Directory, $"{rootFolderName}/{relPath}/")
        {
            ModificationTime = dir.LastWriteTimeUtc,
            Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                   UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                   UnixFileMode.OtherRead | UnixFileMode.OtherExecute
        };
        tarWriter.WriteEntry(dirEntry);
    }

    foreach (var file in rootDirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
    {
        string relPath = Path.GetRelativePath(sourceDirectory, file.FullName).Replace('\\', '/');
        bool isExecutable = IsUnixExecutable(file);

        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                   UnixFileMode.GroupRead |
                   UnixFileMode.OtherRead;

        if (isExecutable)
        {
            mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        }

        using var dataStream = file.OpenRead();
        var fileEntry = new PaxTarEntry(TarEntryType.RegularFile, $"{rootFolderName}/{relPath}")
        {
            DataStream = dataStream,
            ModificationTime = file.LastWriteTimeUtc,
            Mode = mode
        };
        tarWriter.WriteEntry(fileEntry);
    }
}

static bool IsUnixExecutable(FileInfo file)
{
    if (string.Equals(file.Name, "Vectorer.App", StringComparison.Ordinal) ||
        string.Equals(file.Name, "createdump", StringComparison.Ordinal))
    {
        return true;
    }

    if (file.Extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".so", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".sh", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return false;
}

static void EnsureUnderDirectory(string candidatePath, string parentPath, string label)
{
    string fullCandidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    string fullParent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    if (!fullCandidate.StartsWith(fullParent + Path.DirectorySeparatorChar, comparison))
    {
        throw new InvalidOperationException($"Refusing to operate on {label} '{fullCandidate}' outside '{fullParent}'.");
    }
}

static void VerifyLocalizationResources(string ridPublishDir, string rid)
{
    string mainAssembly = Path.Combine(ridPublishDir, "Vectorer.App.dll");
    string mainExecutable = Path.Combine(ridPublishDir, rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? "Vectorer.App.exe" : "Vectorer.App");
    bool hasMainAssembly = File.Exists(mainAssembly) && new FileInfo(mainAssembly).Length > 0;
    bool hasMainExecutable = File.Exists(mainExecutable) && new FileInfo(mainExecutable).Length > 0;
    if (!hasMainAssembly && !hasMainExecutable)
    {
        throw new InvalidOperationException($"Missing or empty main application assembly in publish output for '{rid}': '{ridPublishDir}'.");
    }

    string jaSatellite = Path.Combine(ridPublishDir, "ja", "Vectorer.App.resources.dll");
    if (File.Exists(jaSatellite))
    {
        long size = new FileInfo(jaSatellite).Length;
        if (size == 0)
        {
            throw new InvalidOperationException($"Japanese satellite resource assembly is empty (0 bytes) for '{rid}': '{jaSatellite}'.");
        }

        Console.WriteLine($"Verified localization resources for {rid}: ja/Vectorer.App.resources.dll ({size} bytes)");
        return;
    }

    if (hasMainExecutable)
    {
        using FileStream exeStream = File.OpenRead(mainExecutable);
        byte[] tailBytes = ReadTailBytes(exeStream);
        if (TryGetBundledEntrySize(tailBytes, "ja/Vectorer.App.resources.dll", out long bundledSize))
        {
            Console.WriteLine($"Verified bundled localization resources for {rid}: ja/Vectorer.App.resources.dll ({bundledSize} bytes in {Path.GetFileName(mainExecutable)})");
            return;
        }
    }

    throw new InvalidOperationException($"Missing Japanese satellite resource assembly for '{rid}': neither '{jaSatellite}' nor a bundled entry in '{Path.GetFileName(mainExecutable)}' was found. Ensure SatelliteResourceLanguages does not strip 'ja'.");
}

static void VerifyArchiveLocalizationResources(string archivePath, string rid, bool isZip)
{
    string expectedPrefix = isZip ? "" : $"Vectorer-{rid}/";
    string expectedSatellite = $"{expectedPrefix}ja/Vectorer.App.resources.dll";
    string expectedAssembly = $"{expectedPrefix}Vectorer.App.dll";
    string expectedExecutable = $"{expectedPrefix}{(rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? "Vectorer.App.exe" : "Vectorer.App")}";

    bool foundMain = false;
    bool foundJaSatellite = false;
    bool foundBundledJaSatellite = false;

    if (isZip)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string entryPath = entry.FullName.Replace('\\', '/');
            if (entryPath.Equals(expectedAssembly, StringComparison.Ordinal))
            {
                if (entry.Length > 0) foundMain = true;
            }
            else if (entryPath.Equals(expectedExecutable, StringComparison.Ordinal))
            {
                if (entry.Length > 0)
                {
                    foundMain = true;
                    using Stream entryStream = entry.Open();
                    byte[] tailBytes = ReadTailBytes(entryStream);
                    if (TryGetBundledEntrySize(tailBytes, "ja/Vectorer.App.resources.dll", out _))
                    {
                        foundBundledJaSatellite = true;
                    }
                }
            }
            else if (entryPath.Equals(expectedSatellite, StringComparison.Ordinal))
            {
                if (entry.Length == 0)
                {
                    throw new InvalidOperationException($"Archive '{archivePath}' contains an empty '{expectedSatellite}'.");
                }
                foundJaSatellite = true;
            }
        }
    }
    else
    {
        using FileStream fileStream = File.OpenRead(archivePath);
        using GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using TarReader tarReader = new TarReader(gzipStream);
        while (tarReader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            {
                continue;
            }

            string entryPath = entry.Name.Replace('\\', '/');
            if (entryPath.Equals(expectedAssembly, StringComparison.Ordinal))
            {
                if (entry.Length > 0) foundMain = true;
            }
            else if (entryPath.Equals(expectedExecutable, StringComparison.Ordinal))
            {
                if (entry.Length > 0)
                {
                    foundMain = true;
                    if (entry.DataStream is not null)
                    {
                        byte[] tailBytes = ReadTailBytes(entry.DataStream);
                        if (TryGetBundledEntrySize(tailBytes, "ja/Vectorer.App.resources.dll", out _))
                        {
                            foundBundledJaSatellite = true;
                        }
                    }
                }
            }
            else if (entryPath.Equals(expectedSatellite, StringComparison.Ordinal))
            {
                if (entry.Length == 0)
                {
                    throw new InvalidOperationException($"Archive '{archivePath}' contains an empty '{expectedSatellite}'.");
                }
                foundJaSatellite = true;
            }
        }
    }

    if (!foundMain)
    {
        throw new InvalidOperationException($"Archive '{archivePath}' is missing a non-empty main application assembly ('{expectedAssembly}' or '{expectedExecutable}').");
    }

    if (!foundJaSatellite && !foundBundledJaSatellite)
    {
        throw new InvalidOperationException($"Archive '{archivePath}' is missing '{expectedSatellite}' (both as a file and as a single-file bundle entry).");
    }

    Console.WriteLine($"Verified archive localization resources: {Path.GetFileName(archivePath)}");
}

static byte[] ReadTailBytes(Stream stream, int maxTailBytes = 1024 * 1024)
{
    if (stream.CanSeek)
    {
        int toRead = (int)Math.Min(maxTailBytes, stream.Length);
        stream.Seek(-toRead, SeekOrigin.End);
        byte[] result = new byte[toRead];
        stream.ReadExactly(result);
        return result;
    }

    byte[] previous = [];
    byte[] current = new byte[maxTailBytes];
    int currentOffset = 0;

    while (true)
    {
        int read = stream.Read(current, currentOffset, current.Length - currentOffset);
        if (read == 0)
        {
            break;
        }

        currentOffset += read;
        if (currentOffset == current.Length)
        {
            previous = current;
            current = new byte[maxTailBytes];
            currentOffset = 0;
        }
    }

    if (previous.Length == 0)
    {
        return current[..currentOffset];
    }

    byte[] combined = new byte[maxTailBytes];
    int fromPrevious = maxTailBytes - currentOffset;
    Buffer.BlockCopy(previous, previous.Length - fromPrevious, combined, 0, fromPrevious);
    Buffer.BlockCopy(current, 0, combined, fromPrevious, currentOffset);
    return combined;
}

static bool TryGetBundledEntrySize(ReadOnlySpan<byte> tailBytes, string relativePath, out long uncompressedSize)
{
    uncompressedSize = 0;
    byte[] pathBytes = Encoding.UTF8.GetBytes(relativePath);
    if (pathBytes.Length >= 128)
    {
        return false;
    }

    // .NET single-file bundle manifest writes each FileEntry as:
    // Offset (Int64), Size (Int64), CompressedSize (Int64, v6+), Type (Byte), RelativePath (7-bit length-prefixed UTF-8)
    byte[] needle = new byte[1 + pathBytes.Length];
    needle[0] = (byte)pathBytes.Length;
    pathBytes.CopyTo(needle.AsSpan(1));

    int matchIndex = tailBytes.LastIndexOf(needle);
    if (matchIndex < 25)
    {
        return false;
    }

    long offset = BinaryPrimitives.ReadInt64LittleEndian(tailBytes.Slice(matchIndex - 25, 8));
    long size = BinaryPrimitives.ReadInt64LittleEndian(tailBytes.Slice(matchIndex - 17, 8));
    long compressedSize = BinaryPrimitives.ReadInt64LittleEndian(tailBytes.Slice(matchIndex - 9, 8));
    byte fileType = tailBytes[matchIndex - 1];

    if (offset <= 0 || size <= 0 || compressedSize < 0 || fileType == 0)
    {
        return false;
    }

    uncompressedSize = size;
    return true;
}
