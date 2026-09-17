using System.IO.Compression;
using System.Net.Sockets;
using System.Security.Authentication;
using FluentFTP;
using FluentFTP.Exceptions;
using BackupFtpConfig = PlexBackup.Resources.FtpConfig;

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

    public bool Upload(DirectoryInfo archiveDirectory, BackupFtpConfig config)
    {
        ArgumentNullException.ThrowIfNull(archiveDirectory);
        ArgumentNullException.ThrowIfNull(config);

        string archivePath = Path.Combine(archiveDirectory.FullName, ArchiveFileName);
        if (!File.Exists(archivePath))
        {
            Console.Error.WriteLine($"Archive to upload not found: {archivePath}");
            return false;
        }

        if (!TryParseFtpServer(config.server, out Uri serverUri))
        {
            Console.Error.WriteLine($"Invalid FTP server: {config.server}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(config.username) || string.IsNullOrWhiteSpace(config.password))
        {
            Console.Error.WriteLine("FTP username and password are required.");
            return false;
        }

        string remoteDirectory = Uri.UnescapeDataString(serverUri.AbsolutePath).TrimEnd('/');
        string remotePath = $"{remoteDirectory}/{ArchiveFileName}";

        Console.WriteLine($"Uploading {archivePath} to ftp://{serverUri.Authority}{remotePath}");

        try
        {
            using var client = new FtpClient(
                serverUri.Host,
                config.username,
                config.password,
                serverUri.Port);

            client.AutoConnect();
            FtpStatus status = client.UploadFile(
                archivePath,
                remotePath,
                FtpRemoteExists.Overwrite,
                createRemoteDir: true,
                FtpVerify.Retry);

            if (status != FtpStatus.Success)
            {
                Console.Error.WriteLine($"FTP upload failed with status: {status}");
                return false;
            }

            Console.WriteLine("FTP upload completed successfully.");
            return true;
        }
        catch (Exception exception) when (exception is FtpException
                                          or IOException
                                          or SocketException
                                          or AuthenticationException
                                          or TimeoutException)
        {
            Console.Error.WriteLine($"FTP upload failed: {exception.Message}");
            return false;
        }
    }

    private static bool TryParseFtpServer(string server, out Uri serverUri)
    {
        serverUri = null!;
        if (string.IsNullOrWhiteSpace(server))
        {
            return false;
        }

        string serverWithScheme = server.Contains("://", StringComparison.Ordinal)
            ? server
            : $"ftp://{server}";

        if (!Uri.TryCreate(serverWithScheme, UriKind.Absolute, out Uri? parsedUri)
            || parsedUri.Scheme != Uri.UriSchemeFtp
            || string.IsNullOrWhiteSpace(parsedUri.Host))
        {
            return false;
        }

        serverUri = parsedUri;
        return true;
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
