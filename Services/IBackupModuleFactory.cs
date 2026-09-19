using PlexBackup.Modules;
using PlexBackup.Resources;

namespace PlexBackup.Services;

public interface IBackupModuleFactory
{
    IBackupModule Create(string moduleName, ModulesConfig config);
}
