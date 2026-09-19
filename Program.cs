using System.CommandLine;
using PlexBackup.Commands;
using PlexBackup.Services;

var credentialStore = new SystemdCredentialStore();
var moduleFactory = new BackupModuleFactory(credentialStore);
var ftpUploader = new FtpUploader(credentialStore);
var backupRunner = new BackupRunner(moduleFactory, ftpUploader, TimeProvider.System);
var backupCommand = new BackupCommand(backupRunner);

RootCommand rootCommand = new RootCommand("Outil de sauvegarde de services auto-hébergés")
{
    backupCommand
};

return rootCommand.Parse(args).Invoke();
