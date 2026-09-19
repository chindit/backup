using System.IO.Compression;

namespace PlexBackup.Services;

public sealed class ZipArchiveService
{
    public FileInfo Create(
        DirectoryInfo source,
        DirectoryInfo destination,
        string serviceName,
        IReadOnlyCollection<string>? excludeDirectories = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        string sourcePath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(source.FullName));
        string destinationPath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(destination.FullName));

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException(
                $"Source directory not found: {sourcePath}");
        }

        if (!Directory.Exists(destinationPath))
        {
            throw new DirectoryNotFoundException(
                $"Temporary directory not found: {destinationPath}");
        }

        if (IsSameDirectoryOrChild(destinationPath, sourcePath))
        {
            throw new InvalidOperationException(
                "The temporary directory cannot be the source directory or one of its subdirectories.");
        }

        string archivePath = Path.Combine(
            destinationPath,
            $"{serviceName}-{Guid.NewGuid():N}.zip");
        string temporaryArchivePath = archivePath + ".part";

        try
        {
            using (FileStream archiveStream = new(
                       temporaryArchivePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (ZipArchive archive = new(archiveStream, ZipArchiveMode.Create))
            {
                AddDirectoryToArchive(
                    archive,
                    sourcePath,
                    excludeDirectories);
            }

            File.Move(temporaryArchivePath, archivePath);
            return new FileInfo(archivePath);
        }
        catch
        {
            TryDelete(temporaryArchivePath);
            TryDelete(archivePath);
            throw;
        }
    }

    private static void AddDirectoryToArchive(
        ZipArchive archive,
        string sourcePath,
        IReadOnlyCollection<string>? excludeDirectories)
    {
        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var excludedDirectoryNames = new HashSet<string>(
            excludeDirectories?.Where(name => !string.IsNullOrWhiteSpace(name))
            ?? [],
            comparer);
        var directoriesToProcess = new Stack<DirectoryInfo>();
        directoriesToProcess.Push(new DirectoryInfo(sourcePath));

        while (directoriesToProcess.TryPop(out DirectoryInfo? directory))
        {
            foreach (FileInfo file in directory.EnumerateFiles())
            {
                string entryName = ToZipEntryName(
                    Path.GetRelativePath(sourcePath, file.FullName));
                archive.CreateEntryFromFile(
                    file.FullName,
                    entryName,
                    CompressionLevel.Optimal);
            }

            foreach (DirectoryInfo childDirectory in directory.EnumerateDirectories())
            {
                if (excludedDirectoryNames.Contains(childDirectory.Name))
                {
                    continue;
                }

                string entryName = ToZipEntryName(
                    Path.GetRelativePath(sourcePath, childDirectory.FullName));
                archive.CreateEntry($"{entryName}/");
                directoriesToProcess.Push(childDirectory);
            }
        }
    }

    private static string ToZipEntryName(string relativePath)
    {
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static bool IsSameDirectoryOrChild(
        string candidatePath,
        string parentPath)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(candidatePath, parentPath, comparison)
               || candidatePath.StartsWith(
                   parentPath + Path.DirectorySeparatorChar,
                   comparison);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"Could not remove temporary file {path}: {exception.Message}");
        }
    }
}
