using PlexBackup.Models;
using PlexBackup.Resources;

namespace PlexBackup.Services;

public interface IBackupRunner
{
    Task<IReadOnlyList<ModuleResult>> RunAsync(
        AppConfig config,
        IReadOnlyCollection<string> requestedModules,
        CancellationToken cancellationToken);
}
