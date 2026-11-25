using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moq;
using PlexBackup.Models;
using PlexBackup.Modules;
using PlexBackup.Resources;
using PlexBackup.Services;
using Xunit;

namespace PlexBackup.Test;

public sealed class SiloBackupModuleTests : IDisposable
{
    private const string BackupId = "20261125T040000.123Z-manual";
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(), $"PlexBackup.SiloTests.{Guid.NewGuid():N}");

    public SiloBackupModuleTests() => Directory.CreateDirectory(_testDirectory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateBackupAsync_ArchivesFullNativeBackup(bool encrypted)
    {
        using var handler = new SiloHandler(encrypted);
        using var client = new HttpClient(handler);
        var module = CreateModule(client);

        BackupArtifact artifact = await module.CreateBackupAsync(CancellationToken.None);

        Assert.Equal("silo", artifact.ServiceName);
        Assert.True(artifact.DeleteAfterRun);
        Assert.EndsWith(".zip", artifact.File.Name);
        using ZipArchive archive = ZipFile.OpenRead(artifact.File.FullName);
        Assert.Equal(handler.Objects.Count, archive.Entries.Count);
        foreach (var (name, content) in handler.Objects)
        {
            using var reader = new StreamReader(Assert.IsType<ZipArchiveEntry>(archive.GetEntry(name)).Open());
            Assert.Equal(content, await reader.ReadToEndAsync());
        }

        Assert.Equal(1, handler.StartCount);
        Assert.Single(Directory.GetFiles(_testDirectory));
    }

    [Fact]
    public async Task CreateBackupAsync_PollsRequestedIdUntilComplete()
    {
        using var handler = new SiloHandler { PendingPolls = 1 };
        using var client = new HttpClient(handler);

        BackupArtifact artifact = await CreateModule(client).CreateBackupAsync(CancellationToken.None);

        Assert.True(artifact.File.Exists);
        Assert.Equal(2, handler.PollCount);
        Assert.Equal(1, handler.StartCount);
    }

    [Fact]
    public async Task CreateBackupAsync_AcceptsCompleteWithWarnings()
    {
        using var handler = new SiloHandler { Status = "complete_with_warnings" };
        using var client = new HttpClient(handler);

        BackupArtifact artifact = await CreateModule(client).CreateBackupAsync(CancellationToken.None);

        Assert.True(artifact.File.Exists);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("unreadable")]
    [InlineData("unverified")]
    public async Task CreateBackupAsync_RejectsUnusableBackup(string status)
    {
        using var handler = new SiloHandler { Status = status };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateModule(client).CreateBackupAsync(CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_testDirectory));
        Assert.Equal(0, handler.DownloadCount);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("interrupted")]
    public async Task CreateBackupAsync_ReportsFailedRun(string status)
    {
        using var handler = new SiloHandler { FailedRun = status, PendingPolls = int.MaxValue };
        using var client = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateModule(client).CreateBackupAsync(CancellationToken.None));

        Assert.Contains("database unavailable", error.Message);
        Assert.Empty(Directory.GetFiles(_testDirectory));
    }

    [Fact]
    public async Task CreateBackupAsync_DoesNotStartWhenCapabilityUnavailable()
    {
        using var handler = new SiloHandler { Available = false };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateModule(client).CreateBackupAsync(CancellationToken.None));

        Assert.Equal(0, handler.StartCount);
    }

    [Fact]
    public async Task CreateBackupAsync_DoesNotRetryConflictingCreation()
    {
        using var handler = new SiloHandler { StartStatus = HttpStatusCode.Conflict };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => CreateModule(client).CreateBackupAsync(CancellationToken.None));

        Assert.Equal(1, handler.StartCount);
        Assert.Equal(0, handler.PollCount);
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("truncated")]
    [InlineData("http-error")]
    [InlineData("cancel")]
    public async Task CreateBackupAsync_RemovesPartialArchiveOnDownloadFailure(string failure)
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new SiloHandler { DownloadFailure = failure, Cancellation = cancellation };
        using var client = new HttpClient(handler);

        Exception? error = await Record.ExceptionAsync(
            () => CreateModule(client).CreateBackupAsync(cancellation.Token));

        Assert.NotNull(error);
        if (failure == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
        else if (failure == "http-error") Assert.IsType<HttpRequestException>(error);
        else Assert.IsType<InvalidDataException>(error);
        Assert.Empty(Directory.GetFiles(_testDirectory));
    }

    [Theory]
    [InlineData("../db.dump")]
    [InlineData("db.dump")]
    [InlineData("missing")]
    public async Task CreateBackupAsync_RejectsUnsafeDuplicateOrMissingObjects(string invalidName)
    {
        using var handler = new SiloHandler { InvalidName = invalidName };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateModule(client).CreateBackupAsync(CancellationToken.None));

        Assert.Equal(0, handler.DownloadCount);
        Assert.Empty(Directory.GetFiles(_testDirectory));
    }

    [Fact]
    public async Task CreateBackupAsync_TimesOutWaitingForBackup()
    {
        using var handler = new SiloHandler { PendingPolls = int.MaxValue };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<TimeoutException>(
            () => CreateModule(client, timeoutSeconds: 1).CreateBackupAsync(CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_testDirectory));
        Assert.Equal(1, handler.StartCount);
    }

    [Fact]
    public async Task CreateBackupAsync_HonorsCancellationBeforeStarting()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var handler = new SiloHandler();
        using var client = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateModule(client).CreateBackupAsync(cancellation.Token));

        Assert.Equal(0, handler.StartCount);
    }

    [Fact]
    public void ConfigLoaderAndFactory_SupportSiloConfiguration()
    {
        string path = Path.Combine(_testDirectory, "backup.json");
        File.WriteAllText(path, """
            {"modules":{"silo":{"server":"http://localhost:8090","apiKeyCredential":"silo_api_key",
            "tempDirectory":"/var/tmp","timeoutSeconds":120,"pollIntervalSeconds":2}}}
            """);

        AppConfig config = ConfigLoader.Load(new FileInfo(path));
        var factory = new BackupModuleFactory(Mock.Of<ICredentialStore>());

        Assert.Equal(["silo"], config.Modules.ConfiguredNames());
        Assert.True(config.Modules.IsConfigured("silo"));
        Assert.Equal(120, config.Modules.Silo!.TimeoutSeconds);
        Assert.Equal(2, config.Modules.Silo.PollIntervalSeconds);
        Assert.IsType<SiloBackupModule>(factory.Create("silo", config.Modules));
    }

    public void Dispose() => Directory.Delete(_testDirectory, recursive: true);

    private SiloBackupModule CreateModule(HttpClient client, int timeoutSeconds = 30)
    {
        var credentials = new Mock<ICredentialStore>(MockBehavior.Strict);
        credentials.Setup(value => value.GetRequired("silo_api_key")).Returns("silo-secret");
        return new SiloBackupModule(new SiloModuleConfig
        {
            Server = "http://silo.test/prefix",
            ApiKeyCredential = "silo_api_key",
            TempDirectory = _testDirectory,
            TimeoutSeconds = timeoutSeconds,
            PollIntervalSeconds = 1
        }, credentials.Object, client);
    }

    private sealed class SiloHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Objects { get; }
        public int PendingPolls { get; init; }
        public string Status { get; init; } = "complete";
        public string? FailedRun { get; init; }
        public bool Available { get; init; } = true;
        public HttpStatusCode StartStatus { get; init; } = HttpStatusCode.Accepted;
        public string? DownloadFailure { get; init; }
        public string? InvalidName { get; init; }
        public CancellationTokenSource? Cancellation { get; init; }
        public int StartCount { get; private set; }
        public int PollCount { get; private set; }
        public int DownloadCount { get; private set; }
        private readonly bool _encrypted;

        public SiloHandler(bool encrypted = false)
        {
            _encrypted = encrypted;
            string suffix = encrypted ? ".age" : "";
            Objects = new Dictionary<string, string>
            {
                ["manifest.json"] = "{\"format_version\":3}",
                ["db.dump" + suffix] = "database",
                ["blobs.tar" + suffix] = "uploaded files",
                ["restore-set.tar" + suffix] = "restore set"
            };
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            string path = uri.AbsolutePath;
            const string apiPath = "/prefix/api/v2/admin/backups";
            if (path.Contains("/objects/"))
            {
                Assert.Null(request.Headers.Authorization);
                Assert.Equal(HttpMethod.Get, request.Method);
                DownloadCount++;
                if (DownloadCount == 2)
                {
                    if (DownloadFailure == "http-error")
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    if (DownloadFailure == "cancel")
                    {
                        Cancellation!.Cancel();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                string content = Objects[Uri.UnescapeDataString(path.Split('/')[^1])];
                if (DownloadCount == 2 && DownloadFailure == "corrupt") content = new string('x', content.Length);
                if (DownloadCount == 2 && DownloadFailure == "truncated") content = content[..^1];
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) };
            }

            Assert.Equal("silo.test", uri.Host);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("silo-secret", request.Headers.Authorization?.Parameter);
            if (path == apiPath + "/capabilities")
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                return Json(new { state = Available ? "available" : "not_configured", allowed = true, download = true });
            }

            if (path == apiPath && request.Method == HttpMethod.Post)
            {
                StartCount++;
                return Json(new { id = BackupId, kind = "manual", status = "in_progress" }, StartStatus);
            }

            if (path == apiPath && request.Method == HttpMethod.Get)
            {
                PollCount++;
                var objects = Objects.Where(pair => pair.Key != "manifest.json").Select(pair => new
                {
                    name = pair.Key,
                    size_bytes = Encoding.UTF8.GetByteCount(pair.Value),
                    sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Value)))
                });
                return Json(new
                {
                    items = new[]
                    {
                        new { id = "older-backup", status = "complete", encrypted = _encrypted, objects },
                        new { id = BackupId, status = PollCount <= PendingPolls ? "in_progress" : Status,
                            encrypted = _encrypted, objects }
                    },
                    last_run = FailedRun is null ? null : new { id = BackupId, status = FailedRun, error = "database unavailable" }
                });
            }

            Assert.Equal(apiPath + "/" + BackupId + "/download-links", path);
            Assert.Equal(HttpMethod.Post, request.Method);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("full", body.RootElement.GetProperty("scope").GetString());
            return Json(new
            {
                items = Objects.Where(pair => InvalidName != "missing" || pair.Key != "manifest.json")
                    .Select((pair, index) => new
                    {
                        name = index == 0 && InvalidName is not null ? InvalidName : pair.Key,
                        size_bytes = Encoding.UTF8.GetByteCount(pair.Value),
                        url = index == 1
                            ? "https://storage.test/objects/" + pair.Key + "?signed=yes"
                            : "/prefix/api/v2/admin/backups/" + BackupId + "/objects/" + pair.Key + "?signed=yes"
                    })
            });
        }

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
    }
}
