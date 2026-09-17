using System.IO.Compression;
using PlexBackup.Resources;
using PlexBackup.Services;
using Xunit;

namespace PlexBackup.Test;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"PlexBackup.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void Compress_WithValidDirectories_CreatesArchiveWithSourceContents()
    {
        DirectoryInfo source = CreateDirectory("source");
        DirectoryInfo destination = CreateDirectory("destination");
        Directory.CreateDirectory(Path.Combine(source.FullName, "Metadata"));
        File.WriteAllText(Path.Combine(source.FullName, "Preferences.xml"), "preferences");
        File.WriteAllText(Path.Combine(source.FullName, "Metadata", "item.txt"), "metadata");

        var service = new BackupService();

        bool result = service.Compress(source, destination);

        Assert.True(result);
        string archivePath = Path.Combine(destination.FullName, BackupService.ArchiveFileName);
        Assert.True(File.Exists(archivePath));

        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        Assert.Equal("preferences", ReadEntry(archive, "Preferences.xml"));
        Assert.Equal("metadata", ReadEntry(archive, "Metadata/item.txt"));
    }

    [Fact]
    public void Compress_WhenArchiveAlreadyExists_ReplacesIt()
    {
        DirectoryInfo source = CreateDirectory("source");
        DirectoryInfo destination = CreateDirectory("destination");
        string sourceFile = Path.Combine(source.FullName, "data.txt");
        File.WriteAllText(sourceFile, "first version");
        var service = new BackupService();
        Assert.True(service.Compress(source, destination));
        File.WriteAllText(sourceFile, "second version");

        bool result = service.Compress(source, destination);

        Assert.True(result);
        using ZipArchive archive = ZipFile.OpenRead(
            Path.Combine(destination.FullName, BackupService.ArchiveFileName));
        Assert.Equal("second version", ReadEntry(archive, "data.txt"));
    }

    [Fact]
    public void Compress_WithMissingSource_ReturnsFalse()
    {
        DirectoryInfo source = new(Path.Combine(_testDirectory, "missing-source"));
        DirectoryInfo destination = CreateDirectory("destination");

        bool result = new BackupService().Compress(source, destination);

        Assert.False(result);
        Assert.False(File.Exists(Path.Combine(destination.FullName, BackupService.ArchiveFileName)));
    }

    [Fact]
    public void Compress_WithDestinationInsideSource_ReturnsFalse()
    {
        DirectoryInfo source = CreateDirectory("source");
        DirectoryInfo destination = Directory.CreateDirectory(Path.Combine(source.FullName, "destination"));
        File.WriteAllText(Path.Combine(source.FullName, "data.txt"), "content");

        bool result = new BackupService().Compress(source, destination);

        Assert.False(result);
        Assert.False(File.Exists(Path.Combine(destination.FullName, BackupService.ArchiveFileName)));
    }

    [Fact]
    public void Upload_WithMissingArchive_ReturnsFalse()
    {
        DirectoryInfo archiveDirectory = CreateDirectory("destination");

        bool result = new BackupService().Upload(archiveDirectory, CreateFtpConfig());

        Assert.False(result);
    }

    [Fact]
    public void Upload_WithInvalidServer_ReturnsFalseWithoutConnecting()
    {
        DirectoryInfo archiveDirectory = CreateDirectory("destination");
        File.WriteAllText(
            Path.Combine(archiveDirectory.FullName, BackupService.ArchiveFileName),
            "archive");
        var config = new FtpConfig
        {
            server = "http://not-an-ftp-server.example.com",
            username = "test-user",
            password = "test-password"
        };

        bool result = new BackupService().Upload(archiveDirectory, config);

        Assert.False(result);
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

    private static string ReadEntry(ZipArchive archive, string entryName)
    {
        ZipArchiveEntry entry = Assert.Single(
            archive.Entries,
            candidate => candidate.FullName == entryName);
        using StreamReader reader = new(entry.Open());
        return reader.ReadToEnd();
    }

    private static FtpConfig CreateFtpConfig()
    {
        return new FtpConfig
        {
            server = "ftp.example.com",
            username = "test-user",
            password = "test-password"
        };
    }
}
