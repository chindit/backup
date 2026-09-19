using Moq;
using PlexBackup.Models;
using PlexBackup.Modules;
using PlexBackup.Resources;
using PlexBackup.Services;
using Xunit;

namespace PlexBackup.Test;

public sealed class BackupRunnerTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"PlexBackup.RunnerTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task RunAsync_UsesDatedRemoteNameAndDeletesTemporaryArtifact()
    {
        FileInfo artifact = CreateArtifact("plex-source.zip");
        var module = new Mock<IBackupModule>();
        module.SetupGet(value => value.Name).Returns("plex");
        module.Setup(value => value.CreateBackupAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new BackupArtifact(
                    "plex",
                    artifact,
                    DeleteAfterRun: true));

        var factory = new Mock<IBackupModuleFactory>();
        factory.Setup(value => value.Create(
                "plex",
                It.IsAny<ModulesConfig>()))
            .Returns(module.Object);
        var uploader = new Mock<IFtpUploader>();
        uploader.Setup(value => value.UploadAsync(
                It.IsAny<FileInfo>(),
                It.IsAny<string>(),
                It.IsAny<FtpConfig>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var runner = new BackupRunner(
            factory.Object,
            uploader.Object,
            new FixedTimeProvider(
                new DateTimeOffset(
                    2026,
                    9,
                    19,
                    12,
                    0,
                    0,
                    TimeSpan.Zero)));

        IReadOnlyList<ModuleResult> results = await runner.RunAsync(
            CreateConfig(),
            ["plex"],
            CancellationToken.None);

        Assert.True(Assert.Single(results).Success);
        uploader.Verify(value => value.UploadAsync(
            It.Is<FileInfo>(file => file.FullName == artifact.FullName),
            "plex-19092026.zip",
            It.IsAny<FtpConfig>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.False(File.Exists(artifact.FullName));
    }

    [Fact]
    public async Task RunAsync_WithAbsentRequestedModule_ThrowsBeforeRunning()
    {
        var factory = new Mock<IBackupModuleFactory>();
        var uploader = new Mock<IFtpUploader>();
        var runner = new BackupRunner(
            factory.Object,
            uploader.Object,
            TimeProvider.System);

        InvalidOperationException exception = await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => runner.RunAsync(
                CreateConfig(),
                ["homeassistant"],
                CancellationToken.None));

        Assert.Contains("absent", exception.Message);
        factory.VerifyNoOtherCalls();
        uploader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunAsync_ContinuesAfterAModuleFailure()
    {
        FileInfo jellyfinArtifact = CreateArtifact("jellyfin.zip");
        var failingModule = new Mock<IBackupModule>();
        failingModule.Setup(value => value.CreateBackupAsync(
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("plex failed"));
        var successfulModule = new Mock<IBackupModule>();
        successfulModule.Setup(value => value.CreateBackupAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new BackupArtifact(
                    "jellyfin",
                    jellyfinArtifact,
                    DeleteAfterRun: false));

        var factory = new Mock<IBackupModuleFactory>();
        factory.Setup(value => value.Create(
                "plex",
                It.IsAny<ModulesConfig>()))
            .Returns(failingModule.Object);
        factory.Setup(value => value.Create(
                "jellyfin",
                It.IsAny<ModulesConfig>()))
            .Returns(successfulModule.Object);
        var uploader = new Mock<IFtpUploader>();
        uploader.Setup(value => value.UploadAsync(
                It.IsAny<FileInfo>(),
                It.IsAny<string>(),
                It.IsAny<FtpConfig>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var runner = new BackupRunner(
            factory.Object,
            uploader.Object,
            new FixedTimeProvider(DateTimeOffset.UtcNow));
        AppConfig config = CreateConfig();
        config = new AppConfig
        {
            Ftp = config.Ftp,
            Modules = new ModulesConfig
            {
                Plex = config.Modules.Plex,
                Jellyfin = new JellyfinModuleConfig
                {
                    Server = "http://localhost:8096",
                    ApiKeyCredential = "jellyfin_api_key"
                }
            }
        };

        IReadOnlyList<ModuleResult> results = await runner.RunAsync(
            config,
            [],
            CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.False(results[0].Success);
        Assert.True(results[1].Success);
        uploader.Verify(value => value.UploadAsync(
            jellyfinArtifact,
            It.IsAny<string>(),
            config.Ftp,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private FileInfo CreateArtifact(string name)
    {
        Directory.CreateDirectory(_testDirectory);
        string path = Path.Combine(_testDirectory, name);
        File.WriteAllText(path, "archive");
        return new FileInfo(path);
    }

    private static AppConfig CreateConfig()
    {
        return new AppConfig
        {
            Ftp = new FtpConfig
            {
                Server = "ftp://ftp.example.com/backups",
                Username = "user",
                PasswordCredential = "ftp_password"
            },
            Modules = new ModulesConfig
            {
                Plex = new PlexModuleConfig
                {
                    SourceDirectory = "/srv/plex",
                    TempDirectory = "/var/tmp"
                }
            }
        };
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
