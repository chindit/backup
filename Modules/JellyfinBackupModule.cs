using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using PlexBackup.Models;
using PlexBackup.Resources;
using PlexBackup.Services;

namespace PlexBackup.Modules;

public sealed class JellyfinBackupModule : IBackupModule
{
    private readonly JellyfinModuleConfig _config;
    private readonly ICredentialStore _credentialStore;
    private readonly HttpClient _httpClient;

    public JellyfinBackupModule(
        JellyfinModuleConfig config,
        ICredentialStore credentialStore,
        HttpClient? httpClient = null)
    {
        _config = config;
        _credentialStore = credentialStore;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public string Name => "jellyfin";

    public async Task<BackupArtifact> CreateBackupAsync(
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.ApiKeyCredential))
        {
            throw new InvalidOperationException(
                "Jellyfin apiKeyCredential is required.");
        }

        Uri endpoint = BuildEndpoint(_config.Server, "Backup/Create");
        string apiKey = _credentialStore.GetRequired(
            _config.ApiKeyCredential);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("X-Emby-Token", apiKey);
        request.Content = JsonContent.Create(new
        {
            Metadata = _config.Metadata,
            Trickplay = _config.Trickplay,
            Subtitles = _config.Subtitles,
            Database = true
        });

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream responseStream = await response.Content
            .ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(
            responseStream,
            cancellationToken: cancellationToken);

        string? path = GetStringProperty(
            document.RootElement,
            "Path",
            "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException(
                "Jellyfin did not return the path of the generated backup.");
        }

        var backup = new FileInfo(path);
        if (!backup.Exists || backup.Length == 0)
        {
            throw new FileNotFoundException(
                $"Jellyfin backup was not found locally: {backup.FullName}",
                backup.FullName);
        }

        using (ZipArchive archive = ZipFile.OpenRead(backup.FullName))
        {
            if (archive.GetEntry("manifest.json") is null)
            {
                throw new InvalidDataException(
                    "The Jellyfin backup does not contain manifest.json.");
            }
        }

        return new BackupArtifact(Name, backup, DeleteAfterRun: false);
    }

    private static Uri BuildEndpoint(string server, string relativePath)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out Uri? baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"Invalid Jellyfin server URL: {server}");
        }

        return new Uri(
            baseUri.AbsoluteUri.TrimEnd('/') + "/" + relativePath);
    }

    private static string? GetStringProperty(
        JsonElement element,
        params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }
}
