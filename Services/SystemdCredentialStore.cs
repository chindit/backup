namespace PlexBackup.Services;

public sealed class SystemdCredentialStore : ICredentialStore
{
    private readonly string? _credentialsDirectory;

    public SystemdCredentialStore(string? credentialsDirectory = null)
    {
        _credentialsDirectory = credentialsDirectory;
    }

    public string GetRequired(string credentialName)
    {
        if (string.IsNullOrWhiteSpace(credentialName)
            || credentialName.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || Path.GetFileName(credentialName) != credentialName)
        {
            throw new InvalidOperationException(
                $"Invalid systemd credential name: {credentialName}");
        }

        string? credentialsDirectory = _credentialsDirectory
            ?? Environment.GetEnvironmentVariable("CREDENTIALS_DIRECTORY");
        if (string.IsNullOrWhiteSpace(credentialsDirectory))
        {
            throw new InvalidOperationException(
                "CREDENTIALS_DIRECTORY is not set. Run the program from a systemd unit using LoadCredentialEncrypted.");
        }

        string credentialPath = Path.Combine(credentialsDirectory, credentialName);
        try
        {
            string value = File.ReadAllText(credentialPath)
                .TrimEnd((char)13, (char)10);
            return string.IsNullOrEmpty(value)
                ? throw new InvalidOperationException(
                    $"Systemd credential is empty: {credentialName}")
                : value;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Could not read systemd credential '{credentialName}': {exception.Message}",
                exception);
        }
    }
}
