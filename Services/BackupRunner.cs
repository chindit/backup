using PlexBackup.Models;
using PlexBackup.Modules;
using PlexBackup.Resources;

namespace PlexBackup.Services;

public sealed class BackupRunner : IBackupRunner
{
    private static readonly HashSet<string> SupportedModules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "plex",
            "jellyfin",
            "homeassistant"
        };

    private readonly IBackupModuleFactory _moduleFactory;
    private readonly IFtpUploader _ftpUploader;
    private readonly TimeProvider _timeProvider;

    public BackupRunner(
        IBackupModuleFactory moduleFactory,
        IFtpUploader ftpUploader,
        TimeProvider timeProvider)
    {
        _moduleFactory = moduleFactory;
        _ftpUploader = ftpUploader;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ModuleResult>> RunAsync(
        AppConfig config,
        IReadOnlyCollection<string> requestedModules,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(requestedModules);

        IReadOnlyList<string> selectedModules = ResolveSelection(
            config.Modules,
            requestedModules);
        var results = new List<ModuleResult>(selectedModules.Count);

        foreach (string moduleName in selectedModules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BackupArtifact? artifact = null;

            try
            {
                IBackupModule module = _moduleFactory.Create(
                    moduleName,
                    config.Modules);
                Console.WriteLine($"[{moduleName}] Creating backup...");
                artifact = await module.CreateBackupAsync(
                    cancellationToken);

                artifact.File.Refresh();
                if (!artifact.File.Exists || artifact.File.Length == 0)
                {
                    throw new InvalidDataException(
                        $"Module '{moduleName}' returned a missing or empty file.");
                }

                string extension = Path.GetExtension(
                    artifact.File.Name);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    throw new InvalidDataException(
                        $"Backup file has no extension: {artifact.File.FullName}");
                }

                string remoteFileName =
                    $"{artifact.ServiceName}-" +
                    $"{_timeProvider.GetLocalNow():ddMMyyyy}" +
                    extension.ToLowerInvariant();

                Console.WriteLine(
                    $"[{moduleName}] Uploading as {remoteFileName}...");
                await _ftpUploader.UploadAsync(
                    artifact.File,
                    remoteFileName,
                    config.Ftp,
                    cancellationToken);

                results.Add(new ModuleResult(
                    moduleName,
                    Success: true,
                    $"Uploaded as {remoteFileName}"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(new ModuleResult(
                    moduleName,
                    Success: false,
                    exception.Message));
            }
            finally
            {
                if (artifact?.DeleteAfterRun == true)
                {
                    TryDelete(artifact.File);
                }
            }
        }

        return results;
    }

    private static IReadOnlyList<string> ResolveSelection(
        ModulesConfig modules,
        IReadOnlyCollection<string> requestedModules)
    {
        IReadOnlyList<string> configured = modules.ConfiguredNames();
        if (requestedModules.Count == 0)
        {
            if (configured.Count == 0)
            {
                throw new InvalidOperationException(
                    "No backup module is configured.");
            }

            return configured;
        }

        var selected = new List<string>();
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (string requested in requestedModules)
        {
            string name = requested.Trim().ToLowerInvariant();
            if (!SupportedModules.Contains(name))
            {
                throw new InvalidOperationException(
                    $"Unknown module '{requested}'. Supported modules: " +
                    string.Join(", ", SupportedModules.Order()));
            }

            if (!modules.IsConfigured(name))
            {
                throw new InvalidOperationException(
                    $"Module '{name}' was requested but is absent from the configuration.");
            }

            if (seen.Add(name))
            {
                selected.Add(name);
            }
        }

        return selected;
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"Could not remove temporary backup {file.FullName}: {exception.Message}");
        }
    }
}
