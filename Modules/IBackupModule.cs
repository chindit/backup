using PlexBackup.Models;

namespace PlexBackup.Modules;

public interface IBackupModule
{
    string Name { get; }
    Task<BackupArtifact> CreateBackupAsync(CancellationToken cancellationToken);
}
