namespace PlexBackup.Resources;

public sealed class FtpConfig
{
    public required string server { get; init; }
    public required string username { get; init; }
    public required string password { get; init; }
}