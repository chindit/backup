using System.IO.Compression;
using PlexBackup.Services;
using Xunit;

namespace PlexBackup.Test;

public sealed class ZipArchiveServiceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"PlexBackup.ZipTests.{Guid.NewGuid():N}");

    [Fact]
    public void Create_WithValidDirectories_CreatesArchive()
    {
        DirectoryInfo source = CreateDirectory("source");
        DirectoryInfo destination = CreateDirectory("destination");
        Directory.CreateDirectory(
            Path.Combine(source.FullName, "Metadata"));
        File.WriteAllText(
            Path.Combine(source.FullName, "Preferences.xml"),
            "preferences");
        File.WriteAllText(
            Path.Combine(source.FullName, "Metadata", "item.txt"),
            "metadata");

        FileInfo result = new ZipArchiveService().Create(
            source,
            destination,
            "plex");

        Assert.True(result.Exists);
        Assert.StartsWith("plex-", result.Name);
        Assert.EndsWith(".zip", result.Name);

        using ZipArchive archive = ZipFile.OpenRead(result.FullName);
        Assert.Equal(
            "preferences",
            ReadEntry(archive, "Preferences.xml"));
        Assert.Equal(
            "metadata",
            ReadEntry(archive, "Metadata/item.txt"));
    }

    [Fact]
    public void Create_WithExcludedDirectories_OmitsTheirContents()
    {
        DirectoryInfo source = CreateDirectory("source");
        DirectoryInfo destination = CreateDirectory("destination");
        Directory.CreateDirectory(
            Path.Combine(source.FullName, "Cache"));
        Directory.CreateDirectory(
            Path.Combine(source.FullName, "Metadata", "Keep"));
        File.WriteAllText(
            Path.Combine(source.FullName, "Cache", "cached.txt"),
            "excluded");
        File.WriteAllText(
            Path.Combine(
                source.FullName,
                "Metadata",
                "Keep",
                "kept.txt"),
            "included");

        FileInfo result = new ZipArchiveService().Create(
            source,
            destination,
            "plex",
            ["Cache"]);

        using ZipArchive archive = ZipFile.OpenRead(result.FullName);
        Assert.DoesNotContain(
            archive.Entries,
            entry => entry.FullName.StartsWith("Cache/"));
        Assert.Equal(
            "included",
            ReadEntry(archive, "Metadata/Keep/kept.txt"));
    }

    [Fact]
    public void Create_WithDestinationInsideSource_Throws()
    {
        DirectoryInfo source = CreateDirectory("source");
        DirectoryInfo destination = Directory.CreateDirectory(
            Path.Combine(source.FullName, "destination"));

        InvalidOperationException exception = Assert.Throws<
            InvalidOperationException>(
            () => new ZipArchiveService().Create(
                source,
                destination,
                "plex"));

        Assert.Contains("cannot be the source", exception.Message);
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
        return Directory.CreateDirectory(
            Path.Combine(_testDirectory, name));
    }

    private static string ReadEntry(
        ZipArchive archive,
        string entryName)
    {
        ZipArchiveEntry entry = Assert.Single(
            archive.Entries,
            candidate => candidate.FullName == entryName);
        using StreamReader reader = new(entry.Open());
        return reader.ReadToEnd();
    }
}
