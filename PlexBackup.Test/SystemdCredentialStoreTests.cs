using PlexBackup.Services;
using Xunit;

namespace PlexBackup.Test;

public sealed class SystemdCredentialStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"PlexBackup.CredentialTests.{Guid.NewGuid():N}");

    [Fact]
    public void GetRequired_ReadsCredentialAndTrimsOnlyLineEndings()
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(
            Path.Combine(_testDirectory, "ftp_password"),
            " secret with spaces " + Environment.NewLine);
        var store = new SystemdCredentialStore(_testDirectory);

        string result = store.GetRequired("ftp_password");

        Assert.Equal(" secret with spaces ", result);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("folder/secret")]
    [InlineData("")]
    public void GetRequired_RejectsInvalidNames(string name)
    {
        var store = new SystemdCredentialStore(_testDirectory);

        Assert.Throws<InvalidOperationException>(
            () => store.GetRequired(name));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
