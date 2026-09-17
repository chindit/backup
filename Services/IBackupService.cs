namespace PlexBackup.Services;

public interface IBackupService
{
    int Backup(DirectoryInfo source, DirectoryInfo destination);
    bool Compress(DirectoryInfo source, DirectoryInfo destination);
}
