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

    private static bool TryReadConfig(FileInfo configFile, out BackupConfig config)
    {
        config = null!;

        if (!configFile.Exists)
        {
            Console.Error.WriteLine("Configuration file not found. Looked in {0}", configFile.FullName);
            return false;
        }

        try
        {
            BackupConfig? deserializedConfig = JsonSerializer.Deserialize<BackupConfig>(
                File.ReadAllText(configFile.FullName));

            if (deserializedConfig is null || deserializedConfig.ftp is null)
            {
                Console.Error.WriteLine("Configuration file is empty or invalid: {0}", configFile.FullName);
                return false;
            }

            config = deserializedConfig;
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or JsonException)
        {
            Console.Error.WriteLine("Could not read configuration file {0}: {1}", configFile.FullName, exception.Message);
            return false;
        }
    }

    private static bool TryResolvePaths(
        BackupConfig config,
        DirectoryInfo? source,
        DirectoryInfo? destination,
        out DirectoryInfo resolvedSource,
        out DirectoryInfo resolvedDestination)
    {
        try
        {
            resolvedSource = source ?? new DirectoryInfo(config.sourceDirectory);
            resolvedDestination = destination ?? new DirectoryInfo(config.tempDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"Invalid storage path in configuration: {exception.Message}");
            resolvedSource = null!;
            resolvedDestination = null!;
            return false;
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

        if (!TryReadConfig(configFile, out BackupConfig config)
            || !TryResolvePaths(config, source, destination, out source, out destination))
        {
            return 1;
        }

        if (!_backupService.Compress(source, destination, config.excludeDirectories ?? []))
        {
            return 1;
        }

        return _backupService.Upload(destination, config.ftp) ? 0 : 1;
    }
}
