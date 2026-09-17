using System.IO.Compression;

namespace PlexBackup.Services;

public sealed class BackupService : IBackupService
{
    public const string ArchiveFileName = "plex-backup.zip";

    public int Backup(DirectoryInfo source, DirectoryInfo destination)
    {
        Console.WriteLine($"Sauvegarde de : {source.FullName}");
        Console.WriteLine($"Destination : {destination.FullName}");

        return 0;
    }

    public bool Compress(DirectoryInfo source, DirectoryInfo destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        string sourcePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.FullName));
        string destinationPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination.FullName));

        if (!Directory.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Source directory not found: {sourcePath}");
            return false;
        }

        if (!Directory.Exists(destinationPath))
        {
            Console.Error.WriteLine($"Destination directory not found: {destinationPath}");
            return false;
        }

        if (IsSameDirectoryOrChild(destinationPath, sourcePath))
        {
            Console.Error.WriteLine("The compression destination cannot be the source directory or one of its subdirectories.");
            return false;
        }

        string archivePath = Path.Combine(destinationPath, ArchiveFileName);
        string temporaryArchivePath = Path.Combine(
            destinationPath,
            $".{ArchiveFileName}.{Guid.NewGuid():N}.tmp");

        Console.WriteLine($"Compressing {sourcePath} to {archivePath}");

        try
        {
            ZipFile.CreateFromDirectory(
                sourcePath,
                temporaryArchivePath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);

            File.Move(temporaryArchivePath, archivePath, overwrite: true);
            Console.WriteLine("Compression completed successfully.");
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or NotSupportedException)
        {
            Console.Error.WriteLine($"Compression failed: {exception.Message}");
            return false;
        }
        finally
        {
            try
            {
                File.Delete(temporaryArchivePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not remove temporary archive {temporaryArchivePath}: {exception.Message}");
            }
        }
    }

    private static bool IsSameDirectoryOrChild(string candidatePath, string parentPath)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(candidatePath, parentPath, comparison))
        {
            return true;
        }

        string parentPathWithSeparator = parentPath + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(parentPathWithSeparator, comparison);
    }
}
