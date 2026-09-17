using System.CommandLine;
using System.Text.Json;
using Moq;
using PlexBackup.Commands;
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
    public void Invoke_WithValidConfiguration_CompressesAndUploadsArchive()
    {
        DirectoryInfo source = CreateDirectory("plex-source");
        DirectoryInfo destination = CreateDirectory("plex-destination");
        FileInfo configFile = CreateConfig(source, destination);
        var backupService = new Mock<IBackupService>();
        backupService
            .Setup(service => service.Compress(
                It.IsAny<DirectoryInfo>(),
                It.IsAny<DirectoryInfo>(),
                It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(true);
        backupService
            .Setup(service => service.Upload(
                It.IsAny<DirectoryInfo>(),
                It.IsAny<FtpConfig>()))
            .Returns(true);

        int exitCode = CreateRootCommand(backupService.Object).Parse(
        [
            "backup",
            "--source", source.FullName,
            "--destination", destination.FullName,
            "--config", configFile.FullName
        ]).Invoke();

        Assert.Equal(0, exitCode);
        backupService.Verify(service => service.Compress(
            It.Is<DirectoryInfo>(directory => directory.FullName == source.FullName),
            It.Is<DirectoryInfo>(directory => directory.FullName == destination.FullName),
            It.Is<IReadOnlyCollection<string>>(excluded => excluded.SequenceEqual(
                new[] { "Cache", "Driver" }))),
            Times.Once);
        backupService.Verify(service => service.Upload(
            It.Is<DirectoryInfo>(directory => directory.FullName == destination.FullName),
            It.Is<FtpConfig>(ftp => ftp.server == "ftp.example.com"
                                    && ftp.username == "test-user"
                                    && ftp.password == "test-password")),
            Times.Once);
    }

    [Fact]
    public void Invoke_WhenCompressionFails_DoesNotUploadArchive()
    {
        DirectoryInfo source = CreateDirectory("plex-source");
        DirectoryInfo destination = CreateDirectory("plex-destination");
        FileInfo configFile = CreateConfig(source, destination);
        var backupService = new Mock<IBackupService>();
        backupService
            .Setup(service => service.Compress(
                It.IsAny<DirectoryInfo>(),
                It.IsAny<DirectoryInfo>(),
                It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(false);

        int exitCode = CreateRootCommand(backupService.Object).Parse(
        [
            "backup",
            "--config", configFile.FullName
        ]).Invoke();

        Assert.Equal(1, exitCode);
        backupService.Verify(service => service.Upload(
            It.IsAny<DirectoryInfo>(),
            It.IsAny<FtpConfig>()),
            Times.Never);
    }

    [Fact]
    public void Invoke_WhenUploadFails_ReturnsFailure()
    {
        DirectoryInfo source = CreateDirectory("plex-source");
        DirectoryInfo destination = CreateDirectory("plex-destination");
        FileInfo configFile = CreateConfig(source, destination);
        var backupService = new Mock<IBackupService>();
        backupService
            .Setup(service => service.Compress(
                It.IsAny<DirectoryInfo>(),
                It.IsAny<DirectoryInfo>(),
                It.IsAny<IReadOnlyCollection<string>>()))
            .Returns(true);
        backupService
            .Setup(service => service.Upload(
                It.IsAny<DirectoryInfo>(),
                It.IsAny<FtpConfig>()))
            .Returns(false);

        int exitCode = CreateRootCommand(backupService.Object).Parse(
        [
            "backup",
            "--config", configFile.FullName
        ]).Invoke();

        Assert.Equal(1, exitCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private DirectoryInfo CreateDirectory(string name)
    {
        return Directory.CreateDirectory(Path.Combine(_testDirectory, name));
    }

    private FileInfo CreateConfig(DirectoryInfo source, DirectoryInfo destination)
    {
        var config = new BackupConfig
        {
            sourceDirectory = source.FullName,
            tempDirectory = destination.FullName,
            excludeDirectories = ["Cache", "Driver"],
            ftp = new FtpConfig
            {
                server = "ftp.example.com",
                username = "test-user",
                password = "test-password"
            }
        };
        string configPath = Path.Combine(_testDirectory, "config.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(config));
        return new FileInfo(configPath);
    }

    private static RootCommand CreateRootCommand(IBackupService backupService)
    {
        return new RootCommand
        {
            new BackupCommand(backupService)
        };
    }
}
