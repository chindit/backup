using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using PlexBackup.Models;
using PlexBackup.Resources;
using PlexBackup.Services;

namespace PlexBackup.Modules;

public sealed class SiloBackupModule : IBackupModule
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    private readonly SiloModuleConfig _config;
    private readonly ICredentialStore _credentialStore;
    private readonly HttpClient _httpClient;

    public SiloBackupModule(
        SiloModuleConfig config,
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

    public string Name => "silo";

    public async Task<BackupArtifact> CreateBackupAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(_config.Server, UriKind.Absolute, out Uri? server)
            || server.Scheme is not ("http" or "https")
            || server.Query.Length != 0 || server.Fragment.Length != 0)
        {
            throw new InvalidOperationException("Invalid Silo server URL.");
        }

        if (string.IsNullOrWhiteSpace(_config.ApiKeyCredential)
            || _config.TimeoutSeconds <= 0 || _config.PollIntervalSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Silo apiKeyCredential and positive timeoutSeconds and pollIntervalSeconds are required.");
        }

        if (string.IsNullOrWhiteSpace(_config.TempDirectory)
            || !Directory.Exists(_config.TempDirectory))
        {
            throw new DirectoryNotFoundException(
                "Silo tempDirectory must be an existing writable directory.");
        }

        var baseUri = new Uri(server.AbsoluteUri.TrimEnd('/') + "/");
        string apiKey = _credentialStore.GetRequired(_config.ApiKeyCredential);
        string finalPath = Path.Combine(
            Path.GetFullPath(_config.TempDirectory),
            $"silo-{Guid.NewGuid():N}.zip");
        string partialPath = finalPath + ".part";
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(_config.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);
        CancellationToken token = linked.Token;

        try
        {
            Capabilities capabilities = await SendJsonAsync<Capabilities>(
                HttpMethod.Get, new Uri(baseUri, "api/v2/admin/backups/capabilities"),
                apiKey, null, token);
            if (capabilities.State != "available"
                || !capabilities.Allowed || !capabilities.Download)
            {
                throw new InvalidOperationException(
                    "Silo backups or backup downloads are not available for this account.");
            }

            Uri backupsUri = new(baseUri, "api/v2/admin/backups");
            // Never retry this POST: the backup continues if the connection drops.
            StartedBackup started = await SendJsonAsync<StartedBackup>(
                HttpMethod.Post, backupsUri, apiKey, null, token,
                HttpStatusCode.Accepted);
            if (string.IsNullOrWhiteSpace(started.Id))
            {
                throw new InvalidDataException("Silo returned no backup id.");
            }

            Backup backup = await WaitForBackupAsync(
                backupsUri, apiKey, started.Id, token);
            DownloadLinks links = await SendJsonAsync<DownloadLinks>(
                HttpMethod.Post,
                new Uri(baseUri,
                    $"api/v2/admin/backups/{Uri.EscapeDataString(started.Id)}/download-links"),
                apiKey, new { scope = "full" }, token);
            ValidateObjects(backup, links);

            using (ZipArchive archive = ZipFile.Open(partialPath, ZipArchiveMode.Create))
            {
                foreach (DownloadLink link in links.Items)
                {
                    if (!Uri.TryCreate(baseUri, link.Url, out Uri? downloadUri)
                        || downloadUri.Scheme is not ("http" or "https")
                        || string.IsNullOrWhiteSpace(link.Url))
                    {
                        throw new InvalidDataException("Silo returned an invalid download URL.");
                    }

                    // Signed links may point to S3. Never send the API key to them.
                    using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
                    using HttpResponseMessage response = await _httpClient.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    await using Stream source = await response.Content.ReadAsStreamAsync(token);
                    ZipArchiveEntry entry = archive.CreateEntry(link.Name);
                    await using Stream destination = entry.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    byte[] buffer = new byte[81920];
                    long length = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, token)) != 0)
                    {
                        length += read;
                        if (length > link.SizeBytes)
                        {
                            throw new InvalidDataException($"Silo object size mismatch: {link.Name}");
                        }

                        hash.AppendData(buffer, 0, read);
                        await destination.WriteAsync(buffer.AsMemory(0, read), token);
                    }

                    BackupObject? expected = backup.Objects.SingleOrDefault(
                        value => value.Name == link.Name);
                    if (length != link.SizeBytes
                        || (expected is not null && !string.Equals(
                            Convert.ToHexString(hash.GetHashAndReset()),
                            expected.Sha256, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidDataException(
                            $"Silo object size or SHA-256 mismatch: {link.Name}");
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            File.Move(partialPath, finalPath);
            return new BackupArtifact(Name, new FileInfo(finalPath), DeleteAfterRun: true);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Silo backup exceeded {_config.TimeoutSeconds} seconds. " +
                "A backup already started on Silo may still be running.");
        }
        finally
        {
            TryDelete(partialPath);
        }
    }

    private async Task<Backup> WaitForBackupAsync(
        Uri uri, string apiKey, string id, CancellationToken cancellationToken)
    {
        while (true)
        {
            BackupList listing = await SendJsonAsync<BackupList>(
                HttpMethod.Get, uri, apiKey, null, cancellationToken);
            if (listing.LastRun is { } run && run.Id == id
                && run.Status is "failed" or "interrupted")
            {
                throw new InvalidOperationException($"Silo backup {run.Status}: {run.Error}");
            }

            Backup? backup = listing.Items.FirstOrDefault(value => value.Id == id);
            if (backup is not null)
            {
                if (backup.Status is "complete" or "complete_with_warnings")
                {
                    if (backup.Status == "complete_with_warnings")
                    {
                        Console.Error.WriteLine(
                            $"[silo] Backup {id} completed with warnings; review it in Silo.");
                    }

                    return backup;
                }

                if (backup.Status != "in_progress")
                {
                    throw new InvalidDataException($"Silo backup is {backup.Status}: {id}");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(_config.PollIntervalSeconds), cancellationToken);
        }
    }

    private async Task<T> SendJsonAsync<T>(
        HttpMethod method, Uri uri, string apiKey, object? body,
        CancellationToken cancellationToken, HttpStatusCode? expectedStatus = null)
    {
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (expectedStatus is not null && response.StatusCode != expectedStatus)
        {
            throw new InvalidDataException("Silo returned an unexpected backup response.");
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
               ?? throw new InvalidDataException("Silo returned an empty response.");
    }

    private static void ValidateObjects(Backup backup, DownloadLinks links)
    {
        string suffix = backup.Encrypted ? ".age" : "";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DownloadLink link in links.Items)
        {
            if (string.IsNullOrWhiteSpace(link.Name) || link.Name is "." or ".."
                || link.Name.IndexOfAny(['/', '\\', ':']) >= 0
                || link.SizeBytes <= 0 || !names.Add(link.Name))
            {
                throw new InvalidDataException("Silo returned invalid or duplicate backup objects.");
            }
        }

        if (!names.Contains("manifest.json") || !names.Contains("db.dump" + suffix)
            || !names.Contains("blobs.tar" + suffix)
            || backup.Objects.Length < 2 || links.Items.Length != backup.Objects.Length + 1
            || backup.Objects.Select(value => value.Name).Distinct().Count() != backup.Objects.Length
            || backup.Objects.Any(value => value.Name == "manifest.json"
                || value.Sha256 is null || value.Sha256.Length != 64
                || !links.Items.Any(link => link.Name == value.Name && link.SizeBytes == value.SizeBytes)))
        {
            throw new InvalidDataException("Silo returned an incomplete backup object list.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not remove temporary file {path}: {exception.Message}");
        }
    }

    private sealed record Capabilities(string State, bool Allowed, bool Download);
    private sealed record StartedBackup(string Id);
    private sealed record BackupList(Backup[] Items, BackupRun? LastRun = null);
    private sealed record BackupRun(string Id, string Status, string? Error);
    private sealed record Backup(string Id, string Status, bool Encrypted, BackupObject[] Objects);
    private sealed record BackupObject(string Name, long SizeBytes, string Sha256);
    private sealed record DownloadLinks(DownloadLink[] Items);
    private sealed record DownloadLink(string Name, long SizeBytes, string Url);
}
