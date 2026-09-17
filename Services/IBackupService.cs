using PlexBackup.Resources;

namespace PlexBackup.Services;

public interface IBackupService
{
    int Backup(DirectoryInfo source, DirectoryInfo destination);
    bool Compress(
        DirectoryInfo source,
        DirectoryInfo destination,
        IReadOnlyCollection<string>? excludeDirectories = null);
    bool Upload(DirectoryInfo archiveDirectory, FtpConfig config);
}
