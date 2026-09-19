namespace PlexBackup.Resources;

public sealed class AppConfig
{
    public FtpConfig Ftp { get; init; } = new();
    public ModulesConfig Modules { get; init; } = new();
}
