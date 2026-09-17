using System.CommandLine;
using PlexBackup.Commands;
using PlexBackup.Services;

BackupService backupService = new BackupService();
BackupCommand backupCommand = new BackupCommand(backupService);

RootCommand rootCommand = new RootCommand("Outil de sauvegarde et de restauration de Plex")
{
    backupCommand
};

return rootCommand.Parse(args).Invoke();
