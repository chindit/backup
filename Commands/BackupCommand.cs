using System.CommandLine;
using System.CommandLine.Parsing;
using PlexBackup.Models;
using PlexBackup.Resources;
using PlexBackup.Services;

namespace PlexBackup.Commands;

public sealed class BackupCommand : Command
{
    private readonly Option<FileInfo> _configOption;
    private readonly Option<string[]> _moduleOption;
    private readonly IBackupRunner _backupRunner;

    public BackupCommand(IBackupRunner backupRunner)
        : base("backup", "Crée et envoie les sauvegardes configurées")
    {
        _backupRunner = backupRunner;

        _configOption = new Option<FileInfo>("--config")
        {
            Description = "Fichier de configuration",
            Required = false,
            DefaultValueFactory = _ => new FileInfo(
                "/etc/backup.json")
        };

        _moduleOption = new Option<string[]>("--module")
        {
            Description =
                "Module à lancer (répéter l’option pour en choisir plusieurs)",
            Required = false,
            Arity = ArgumentArity.ZeroOrMore
        };

        Options.Add(_configOption);
        Options.Add(_moduleOption);
        SetAction(Execute);
    }

    private int Execute(ParseResult parseResult)
    {
        FileInfo configFile = parseResult.GetRequiredValue(
            _configOption);
        string[] requestedModules =
            parseResult.GetValue(_moduleOption) ?? [];

        try
        {
            AppConfig config = ConfigLoader.Load(configFile);
            IReadOnlyList<ModuleResult> results = _backupRunner
                .RunAsync(
                    config,
                    requestedModules,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            foreach (ModuleResult result in results)
            {
                TextWriter writer = result.Success
                    ? Console.Out
                    : Console.Error;
                writer.WriteLine(
                    $"[{result.ModuleName}] " +
                    $"{(result.Success ? "OK" : "FAILED")}: " +
                    result.Message);
            }

            return results.All(result => result.Success) ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidDataException
                                          or InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
