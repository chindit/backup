namespace PlexBackup.Resources;

public sealed class BackupConfig
{
    public required string tempDirectory { get; init; }
    public required string sourceDirectory { get; init; }
    public string[]? excludeDirectories { get; init; } = [];
    public required FtpConfig ftp  { get; init; }
}
