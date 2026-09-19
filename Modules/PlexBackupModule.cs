using PlexBackup.Models;
using PlexBackup.Resources;
using PlexBackup.Services;

namespace PlexBackup.Modules;

public sealed class PlexBackupModule : IBackupModule
{
    private readonly PlexModuleConfig _config;
    private readonly ZipArchiveService _archiveService;

    public PlexBackupModule(
        PlexModuleConfig config,
        ZipArchiveService archiveService)
    {
        _config = config;
        _archiveService = archiveService;
    }

    public string Name => "plex";

    public Task<BackupArtifact> CreateBackupAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_config.SourceDirectory)
            || string.IsNullOrWhiteSpace(_config.TempDirectory))
        {
            throw new InvalidOperationException(
                "Plex sourceDirectory and tempDirectory are required.");
        }

        FileInfo archive = _archiveService.Create(
            new DirectoryInfo(_config.SourceDirectory),
            new DirectoryInfo(_config.TempDirectory),
            Name,
            _config.ExcludeDirectories);

        return Task.FromResult(
            new BackupArtifact(Name, archive, DeleteAfterRun: true));
    }
}
