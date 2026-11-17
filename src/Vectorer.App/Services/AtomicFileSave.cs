namespace Vectorer.App.Services;

public static class AtomicFileSave
{
    public static async Task WriteAsync(string path, Func<Stream, Task> writeContents)
    {
        var destination = ResolvePath(path);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(destination)!, $".vectorer-{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Options = FileOptions.Asynchronous
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        var created = false;
        try
        {
            await using (var temporary = new FileStream(temporaryPath, options))
            {
                created = true;
                await writeContents(temporary);
                await temporary.FlushAsync();
            }

            if (File.Exists(destination))
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporaryPath, File.GetUnixFileMode(destination));
                File.Replace(temporaryPath, destination, null);
            }
            else
            {
                File.Move(temporaryPath, destination);
            }
            created = false;
        }
        finally
        {
            if (created)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal static string ResolvePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var parts = fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var resolved = root;
        for (var i = 0; i < parts.Length; i++)
        {
            resolved = Path.Combine(resolved, parts[i]);
            FileSystemInfo info = i == parts.Length - 1 ? new FileInfo(resolved) : new DirectoryInfo(resolved);
            if (info.LinkTarget is not null)
                resolved = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? resolved;
        }
        return resolved;
    }
}
