using System.CommandLine;
using Moq;
using PlexBackup.Commands;
using PlexBackup.Services;
using Xunit;

namespace PlexBackup.Test;

public sealed class BackupCommandTests
{
    [Fact]
    public void Invoke_WithValidOptions_CallsCompress()
    {
        string testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"PlexBackup.CommandTests.{Guid.NewGuid():N}");
        string sourcePath = Directory.CreateDirectory(Path.Combine(testDirectory, "plex-source")).FullName;
        string destinationPath = Directory.CreateDirectory(Path.Combine(testDirectory, "plex-destination")).FullName;

        try
        {
            var backupService = new Mock<IBackupService>();
            backupService
                .Setup(service => service.Compress(
                    It.IsAny<DirectoryInfo>(),
                    It.IsAny<DirectoryInfo>()))
                .Returns(true);

            RootCommand rootCommand = new RootCommand
            {
                new BackupCommand(backupService.Object)
            };

            int exitCode = rootCommand.Parse(
            [
                "backup",
                "--source", sourcePath,
                "--destination", destinationPath
            ]).Invoke();

            Assert.Equal(0, exitCode);
            backupService.Verify(service => service.Compress(
                It.Is<DirectoryInfo>(directory => directory.FullName == sourcePath),
                It.Is<DirectoryInfo>(directory => directory.FullName == destinationPath)),
                Times.Once);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}
