using System.Globalization;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PlexBackup.Models;
using PlexBackup.Resources;
using PlexBackup.Services;

namespace PlexBackup.Modules;

public sealed class HomeAssistantBackupModule : IBackupModule
{
    private const int MaxWebSocketMessageBytes = 4 * 1024 * 1024;

    private readonly HomeAssistantModuleConfig _config;
    private readonly ICredentialStore _credentialStore;
    private readonly HttpClient _httpClient;

    public HomeAssistantBackupModule(
        HomeAssistantModuleConfig config,
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

    public string Name => "homeassistant";

    public async Task<BackupArtifact> CreateBackupAsync(
        CancellationToken cancellationToken)
    {
        Uri baseUri = ParseServerUri(_config.Server);
        if (string.IsNullOrWhiteSpace(_config.TokenCredential))
        {
            throw new InvalidOperationException(
                "Home Assistant tokenCredential is required.");
        }

        string tempDirectory = Path.GetFullPath(
            _config.TempDirectory);
        if (string.IsNullOrWhiteSpace(_config.TempDirectory)
            || !Directory.Exists(tempDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Home Assistant temporary directory not found: {tempDirectory}");
        }

        string token = _credentialStore.GetRequired(
            _config.TokenCredential);
        (string backupId, string agentId) = await FindLatestBackupAsync(
            baseUri,
            token,
            cancellationToken);

        string finalPath = Path.Combine(
            tempDirectory,
            $"homeassistant-{Guid.NewGuid():N}.tar");
        string partialPath = finalPath + ".part";

        Uri downloadUri = BuildHttpEndpoint(
            baseUri,
            $"api/backup/download/{Uri.EscapeDataString(backupId)}" +
            $"?agent_id={Uri.EscapeDataString(agentId)}");
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                downloadUri);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                token);

            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream source = await response.Content
                .ReadAsStreamAsync(cancellationToken);
            await using (var destination = new FileStream(
                             partialPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            if (new FileInfo(partialPath).Length == 0)
            {
                throw new InvalidDataException(
                    "Home Assistant returned an empty backup.");
            }

            File.Move(partialPath, finalPath);
            return new BackupArtifact(
                Name,
                new FileInfo(finalPath),
                DeleteAfterRun: true);
        }
        catch
        {
            TryDelete(partialPath);
            TryDelete(finalPath);
            throw;
        }
    }

    private static async Task<(string BackupId, string AgentId)>
        FindLatestBackupAsync(
            Uri baseUri,
            string token,
            CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(
            BuildWebSocketEndpoint(baseUri),
            cancellationToken);

        using JsonDocument greeting = JsonDocument.Parse(
            await ReceiveTextAsync(socket, cancellationToken));
        EnsureMessageType(greeting.RootElement, "auth_required");

        await SendJsonAsync(
            socket,
            new { type = "auth", access_token = token },
            cancellationToken);

        using JsonDocument authResult = JsonDocument.Parse(
            await ReceiveTextAsync(socket, cancellationToken));
        EnsureMessageType(authResult.RootElement, "auth_ok");

        const int commandId = 1;
        await SendJsonAsync(
            socket,
            new { id = commandId, type = "backup/info" },
            cancellationToken);

        while (true)
        {
            using JsonDocument response = JsonDocument.Parse(
                await ReceiveTextAsync(socket, cancellationToken));
            JsonElement root = response.RootElement;
            if (!root.TryGetProperty("id", out JsonElement id)
                || id.GetInt32() != commandId)
            {
                continue;
            }

            if (!root.TryGetProperty("success", out JsonElement success)
                || !success.GetBoolean())
            {
                string error = root.TryGetProperty(
                    "error",
                    out JsonElement errorElement)
                    ? errorElement.ToString()
                    : "unknown error";
                throw new InvalidOperationException(
                    $"Home Assistant backup/info failed: {error}");
            }

            if (!root.TryGetProperty("result", out JsonElement result)
                || !result.TryGetProperty(
                    "backups",
                    out JsonElement backups)
                || backups.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "Home Assistant returned no backup list.");
            }

            return SelectLatestBackup(backups);
        }
    }

    private static (string BackupId, string AgentId)
        SelectLatestBackup(JsonElement backups)
    {
        string? selectedId = null;
        string? selectedAgent = null;
        DateTimeOffset selectedDate = DateTimeOffset.MinValue;

        foreach (JsonElement backup in backups.EnumerateArray())
        {
            if (!TryGetUsableBackup(
                    backup,
                    out string? backupId,
                    out string? agentId,
                    out DateTimeOffset date)
                || date <= selectedDate)
            {
                continue;
            }

            selectedId = backupId;
            selectedAgent = agentId;
            selectedDate = date;
        }

        if (selectedId is null || selectedAgent is null)
        {
            throw new InvalidDataException(
                "Home Assistant has no complete downloadable backup.");
        }

        return (selectedId, selectedAgent);
    }

    private static bool TryGetUsableBackup(
        JsonElement backup,
        out string? backupId,
        out string? agentId,
        out DateTimeOffset date)
    {
        backupId = GetStringProperty(backup, "backup_id", "slug");
        agentId = null;
        date = DateTimeOffset.MinValue;

        string? dateText = GetStringProperty(backup, "date");
        if (backupId is null
            || dateText is null
            || !DateTimeOffset.TryParse(
                dateText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out date)
            || HasNonEmptyArray(backup, "failed_addons")
            || HasNonEmptyArray(backup, "failed_agent_ids")
            || HasNonEmptyArray(backup, "failed_folders"))
        {
            return false;
        }

        if (backup.TryGetProperty(
                "homeassistant_included",
                out JsonElement included)
            && included.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (backup.TryGetProperty("content", out JsonElement content)
            && content.ValueKind == JsonValueKind.Object
            && content.TryGetProperty(
                "homeassistant",
                out JsonElement homeAssistant)
            && homeAssistant.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (backup.TryGetProperty("agents", out JsonElement agents)
            && agents.ValueKind == JsonValueKind.Object)
        {
            if (agents.TryGetProperty(
                    "hassio.local",
                    out JsonElement unused))
            {
                agentId = "hassio.local";
            }
            else
            {
                foreach (JsonProperty agent in agents.EnumerateObject())
                {
                    agentId = agent.Name;
                    break;
                }
            }
        }

        agentId ??= GetStringProperty(backup, "agent_id")
                    ?? "hassio.local";
        return true;
    }

    private static bool HasNonEmptyArray(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(
                   propertyName,
                   out JsonElement value)
               && value.ValueKind == JsonValueKind.Array
               && value.GetArrayLength() > 0;
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

    private static async Task SendJsonAsync(
        ClientWebSocket socket,
        object payload,
        CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    private static async Task<string> ReceiveTextAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[8192];
        using var message = new MemoryStream();

        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(
                buffer,
                cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException(
                    $"Home Assistant closed the connection: {result.CloseStatusDescription}");
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidDataException(
                    "Home Assistant returned a non-text WebSocket message.");
            }

            message.Write(buffer, 0, result.Count);
            if (message.Length > MaxWebSocketMessageBytes)
            {
                throw new InvalidDataException(
                    "Home Assistant WebSocket response is too large.");
            }

            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(message.ToArray());
            }
        }
    }

    private static void EnsureMessageType(
        JsonElement message,
        string expectedType)
    {
        if (!message.TryGetProperty("type", out JsonElement type)
            || type.GetString() != expectedType)
        {
            throw new InvalidOperationException(
                $"Unexpected Home Assistant authentication response: {message}");
        }
    }

    private static Uri ParseServerUri(string server)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"Home Assistant server URL must use HTTPS: {server}");
        }

        return uri;
    }

    private static Uri BuildHttpEndpoint(
        Uri baseUri,
        string relativePath)
    {
        return new Uri(
            baseUri.AbsoluteUri.TrimEnd('/') + "/" + relativePath);
    }

    private static Uri BuildWebSocketEndpoint(Uri baseUri)
    {
        var builder = new UriBuilder(
            BuildHttpEndpoint(baseUri, "api/websocket"))
        {
            Scheme = baseUri.Scheme == "https" ? "wss" : "ws"
        };
        return builder.Uri;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"Could not remove temporary file {path}: {exception.Message}");
        }
    }
}
