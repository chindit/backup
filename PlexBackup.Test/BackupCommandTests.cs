using System.CommandLine;
using System.Text.Json;
using Moq;
using PlexBackup.Commands;
using PlexBackup.Models;
using PlexBackup.Resources;
using PlexBackup.Services;
using Xunit;

namespace PlexBackup.Test;

public sealed class BackupCommandTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"PlexBackup.CommandTests.{Guid.NewGuid():N}");

    [Fact]
    public void Invoke_WithRepeatedModules_ForwardsSelection()
    {
        FileInfo configFile = CreateConfig();
        IReadOnlyCollection<string>? capturedModules = null;
        var runner = new Mock<IBackupRunner>();
        runner.Setup(service => service.RunAsync(
                It.IsAny<AppConfig>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<AppConfig, IReadOnlyCollection<string>, CancellationToken>(
                (_, modules, _) => capturedModules = modules)
            .ReturnsAsync(
            [
                new ModuleResult("plex", true, "ok"),
                new ModuleResult("jellyfin", true, "ok")
            ]);

        int exitCode = CreateRootCommand(runner.Object).Parse(
        [
            "backup",
            "--config", configFile.FullName,
            "--module", "plex",
            "--module", "jellyfin"
        ]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            ["plex", "jellyfin"],
            capturedModules);
    }

    [Fact]
    public void Invoke_WithoutModule_ForwardsEmptySelection()
    {
        FileInfo configFile = CreateConfig();
        IReadOnlyCollection<string>? capturedModules = null;
        var runner = new Mock<IBackupRunner>();
        runner.Setup(service => service.RunAsync(
                It.IsAny<AppConfig>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback<AppConfig, IReadOnlyCollection<string>, CancellationToken>(
                (_, modules, _) => capturedModules = modules)
            .ReturnsAsync(
            [
                new ModuleResult("plex", true, "ok")
            ]);

        int exitCode = CreateRootCommand(runner.Object).Parse(
        [
            "backup",
            "--config", configFile.FullName
        ]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Empty(capturedModules!);
    }

    [Fact]
    public void Invoke_WhenOneModuleFails_ReturnsFailure()
    {
        FileInfo configFile = CreateConfig();
        var runner = new Mock<IBackupRunner>();
        runner.Setup(service => service.RunAsync(
                It.IsAny<AppConfig>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ModuleResult("plex", false, "failed")
            ]);

        int exitCode = CreateRootCommand(runner.Object).Parse(
        [
            "backup",
            "--config", configFile.FullName
        ]).Invoke();

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Invoke_WithMissingConfig_DoesNotRun()
    {
        var runner = new Mock<IBackupRunner>();
        string missingPath = Path.Combine(
            _testDirectory,
            "missing.json");

        int exitCode = CreateRootCommand(runner.Object).Parse(
        [
            "backup",
            "--config", missingPath
        ]).Invoke();

        Assert.Equal(1, exitCode);
        runner.VerifyNoOtherCalls();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private FileInfo CreateConfig()
    {
        Directory.CreateDirectory(_testDirectory);
        var config = new AppConfig
        {
            Ftp = new FtpConfig
            {
                Server = "ftp://ftp.example.com/backups",
                Username = "test-user",
                PasswordCredential = "ftp_password"
            },
            Modules = new ModulesConfig
            {
                Plex = new PlexModuleConfig
                {
                    SourceDirectory = "/srv/plex",
                    TempDirectory = "/var/tmp",
                    ExcludeDirectories = ["Cache"]
                },
                Jellyfin = new JellyfinModuleConfig
                {
                    Server = "http://localhost:8096",
                    ApiKeyCredential = "jellyfin_api_key"
                }
            }
        };
        string configPath = Path.Combine(
            _testDirectory,
            "config.json");
        File.WriteAllText(
            configPath,
            JsonSerializer.Serialize(config));
        return new FileInfo(configPath);
    }

    private static RootCommand CreateRootCommand(
        IBackupRunner backupRunner)
    {
        return new RootCommand
        {
            new BackupCommand(backupRunner)
        };
    }
}
