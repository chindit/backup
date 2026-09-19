using PlexBackup.Resources;

namespace PlexBackup.Services;

public interface IFtpUploader
{
    Task UploadAsync(
        FileInfo localFile,
        string remoteFileName,
        FtpConfig config,
        CancellationToken cancellationToken);
}
