namespace PlexBackup.Services;

public interface ICredentialStore
{
    string GetRequired(string credentialName);
}
