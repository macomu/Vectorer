#!/usr/bin/env dotnet

using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;

string[] defaultRids = ["win-x64", "win-arm64", "osx-arm64"];
List<string> rids = [];
string configuration = "Release";
string outputRoot = "artifacts";
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
string publishBaseDir = Path.Combine(artifactsDir, "publish");
string packagesDir = Path.Combine(artifactsDir, "packages");

Directory.CreateDirectory(publishBaseDir);
Directory.CreateDirectory(packagesDir);

Console.WriteLine($"Repository Root : {repoRoot}");
Console.WriteLine($"Configuration   : {configuration}");
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
    string ridPublishDir = Path.Combine(publishBaseDir, rid);
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

    RunDotnet(repoRoot, [.. publishArgs]);

    CopyDocIfPresent(repoRoot, ridPublishDir, "LICENSE");
    CopyDocIfPresent(repoRoot, ridPublishDir, "THIRD_PARTY_NOTICES.md");
    CopyDocIfPresent(repoRoot, ridPublishDir, "README.md");

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
