using System.Net.Sockets;
using System.Security.Authentication;
using FluentFTP;
using FluentFTP.Exceptions;
using PlexBackup.Resources;
using BackupFtpConfig = PlexBackup.Resources.FtpConfig;

namespace PlexBackup.Services;

public sealed class FtpUploader : IFtpUploader
{
    private readonly ICredentialStore _credentialStore;

    public FtpUploader(ICredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    public Task UploadAsync(
        FileInfo localFile,
        string remoteFileName,
        BackupFtpConfig config,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(localFile);
        ArgumentNullException.ThrowIfNull(config);
        cancellationToken.ThrowIfCancellationRequested();

        localFile.Refresh();
        if (!localFile.Exists || localFile.Length == 0)
        {
            throw new FileNotFoundException(
                $"Backup file not found or empty: {localFile.FullName}",
                localFile.FullName);
        }

        Uri serverUri = ParseServer(config.Server);
        if (string.IsNullOrWhiteSpace(config.Username))
        {
            throw new InvalidOperationException(
                "FTP username is required.");
        }

        if (string.IsNullOrWhiteSpace(remoteFileName)
            || Path.GetFileName(remoteFileName) != remoteFileName)
        {
            throw new InvalidOperationException(
                $"Invalid remote file name: {remoteFileName}");
        }

        string password = _credentialStore.GetRequired(
            config.PasswordCredential);
        string remoteDirectory = Uri.UnescapeDataString(
                serverUri.AbsolutePath)
            .Trim('/');
        string remotePath = string.IsNullOrEmpty(remoteDirectory)
            ? remoteFileName
            : $"{remoteDirectory}/{remoteFileName}";

        try
        {
            using var client = new FtpClient(
                serverUri.Host,
                config.Username,
                password,
                serverUri.Port);

            client.Config.EncryptionMode = FtpEncryptionMode.None;
            client.Config.CheckCapabilities = false;
            client.Connect();

            FtpStatus status = client.UploadFile(
                localFile.FullName,
                remotePath,
                FtpRemoteExists.NoCheck,
                createRemoteDir: false,
                FtpVerify.None);

            if (status != FtpStatus.Success)
            {
                throw new InvalidOperationException(
                    $"FTP upload failed with status: {status}");
            }

            return Task.CompletedTask;
        }
        catch (Exception exception) when (exception is FtpException
                                          or IOException
                                          or SocketException
                                          or AuthenticationException
                                          or TimeoutException)
        {
            throw new InvalidOperationException(
                $"FTP upload failed: {exception.Message}",
                exception);
        }
    }

    private static Uri ParseServer(string server)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            throw new InvalidOperationException(
                "FTP server is required.");
        }

        string serverWithScheme = server.Contains(
            "://",
            StringComparison.Ordinal)
            ? server
            : $"ftp://{server}";

        if (!Uri.TryCreate(
                serverWithScheme,
                UriKind.Absolute,
                out Uri? serverUri)
            || serverUri.Scheme != Uri.UriSchemeFtp
            || string.IsNullOrWhiteSpace(serverUri.Host))
        {
            throw new InvalidOperationException(
                $"Invalid FTP server: {server}");
        }

        return serverUri;
    }
}
