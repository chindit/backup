using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using PlexBackup.Resources;
using PlexBackup.Services;

namespace PlexBackup.Commands;

public sealed class BackupCommand : Command
{
    private readonly Option<FileInfo> _configOption;
    private readonly Option<DirectoryInfo> _sourceOption;
    private readonly Option<DirectoryInfo> _destinationOption;
    private readonly IBackupService _backupService;

    public BackupCommand(IBackupService backupService)
        : base("backup", "Crée une sauvegarde des données Plex")
    {
        _backupService = backupService;

        _sourceOption = new Option<DirectoryInfo>("--source")
        {
            Description = "Dossier Plex à sauvegarder",
            Required = false
        };

        _destinationOption = new Option<DirectoryInfo>("--destination")
        {
            Description = "Destination de la sauvegarde",
            Required = false
        };

        _configOption = new Option<FileInfo>("--config")
        {
            Description = "Configuration file",
            Required = false,
            DefaultValueFactory = _ => new FileInfo("/etc/plex-backup.json")
        };

        Options.Add(_sourceOption);
        Options.Add(_destinationOption);
        Options.Add(_configOption);

        SetAction(Execute);
    }

    private static bool TryResolvePaths(
        FileInfo config,
        DirectoryInfo? source,
        DirectoryInfo? destination,
        out DirectoryInfo resolvedSource,
        out DirectoryInfo resolvedDestination)
    {
        resolvedSource = source!;
        resolvedDestination = destination!;

        if (source is null || destination is null)
        {
            if (!config.Exists)
            {
                Console.Error.WriteLine("Configuration file not found. Looked in {0}", config.FullName);
                return false;
            }

            try
            {
                BackupConfig? configFile = JsonSerializer.Deserialize<BackupConfig>(
                    File.ReadAllText(config.FullName));

                if (configFile is null)
                {
                    Console.Error.WriteLine("Configuration file is empty or invalid: {0}", config.FullName);
                    return false;
                }

                resolvedSource = source ?? new DirectoryInfo(configFile.sourceDirectory);
                resolvedDestination = destination ?? new DirectoryInfo(configFile.tempDirectory);
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or JsonException)
            {
                Console.Error.WriteLine("Could not read configuration file {0}: {1}", config.FullName, exception.Message);
                return false;
            }
        }

        if (resolvedSource.Exists && resolvedDestination.Exists)
        {
            return true;
        }

        Console.Error.WriteLine(
            "Storage directories are not ok. Either {0} or {1} is missing.",
            resolvedSource.FullName,
            resolvedDestination.FullName);
        return false;
    }

    private int Execute(ParseResult parseResult)
    {
        DirectoryInfo? source = parseResult.GetValue(_sourceOption);
        DirectoryInfo? destination = parseResult.GetValue(_destinationOption);
        FileInfo configFile = parseResult.GetRequiredValue(_configOption);

        if (!TryResolvePaths(configFile, source, destination, out source, out destination))
        {
            return 1;
        }

        bool compressed = _backupService.Compress(source, destination);
        //_backupService.Upload(_destination, _config);

        return compressed ? 0 : 1;
    }
}
