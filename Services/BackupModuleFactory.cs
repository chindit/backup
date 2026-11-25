using PlexBackup.Modules;
using PlexBackup.Resources;

namespace PlexBackup.Services;

public sealed class BackupModuleFactory : IBackupModuleFactory
{
    private readonly ICredentialStore _credentialStore;

    public BackupModuleFactory(ICredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    public IBackupModule Create(
        string moduleName,
        ModulesConfig config)
    {
        return moduleName switch
        {
            "plex" when config.Plex is not null =>
                new PlexBackupModule(
                    config.Plex,
                    new ZipArchiveService()),
            "silo" when config.Silo is not null =>
                new SiloBackupModule(
                    config.Silo,
                    _credentialStore),
            "homeassistant" when config.HomeAssistant is not null =>
                new HomeAssistantBackupModule(
                    config.HomeAssistant,
                    _credentialStore),
            _ => throw new InvalidOperationException(
                $"Module '{moduleName}' is not configured.")
        };
    }
}
