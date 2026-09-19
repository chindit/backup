using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using PlexBackup.Modules;
using PlexBackup.Resources;
using PlexBackup.Services;
using Xunit;

namespace PlexBackup.Test;

public sealed class JellyfinBackupModuleTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"PlexBackup.JellyfinTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateBackupAsync_RequestsNativeBackupAndValidatesManifest()
    {
        Directory.CreateDirectory(_testDirectory);
        string backupPath = Path.Combine(
            _testDirectory,
            "jellyfin-backup.zip");
        using (ZipArchive archive = ZipFile.Open(
                   backupPath,
                   ZipArchiveMode.Create))
        {
            archive.CreateEntry("manifest.json");
        }

        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { Path = backupPath }),
                    Encoding.UTF8,
                    "application/json")
            });
        var credentials = new StubCredentialStore(
            "jellyfin-secret");
        var module = new JellyfinBackupModule(
            new JellyfinModuleConfig
            {
                Server = "http://127.0.0.1:8096",
                ApiKeyCredential = "jellyfin_api_key",
                Metadata = true,
                Subtitles = true,
                Trickplay = false
            },
            credentials,
            new HttpClient(handler));

        var artifact = await module.CreateBackupAsync(
            CancellationToken.None);

        Assert.Equal("jellyfin", artifact.ServiceName);
        Assert.Equal(backupPath, artifact.File.FullName);
        Assert.False(artifact.DeleteAfterRun);
        Assert.Equal(
            new Uri("http://127.0.0.1:8096/Backup/Create"),
            handler.RequestUri);
        Assert.Equal(
            "jellyfin-secret",
            handler.ApiKey);
        using JsonDocument requestBody = JsonDocument.Parse(
            handler.RequestBody);
        Assert.True(
            requestBody.RootElement.GetProperty("database").GetBoolean());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private sealed class StubCredentialStore(
        string secret) : ICredentialStore
    {
        public string GetRequired(string credentialName)
        {
            Assert.Equal("jellyfin_api_key", credentialName);
            return secret;
        }
    }

    private sealed class RecordingHandler(
        HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.TryGetValues(
                "X-Emby-Token",
                out IEnumerable<string>? values)
                ? Assert.Single(values)
                : null;
            RequestBody = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);
            return response;
        }
    }
}
