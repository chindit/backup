namespace PlexBackup.Models;

public sealed record BackupArtifact(
    string ServiceName,
    FileInfo File,
    bool DeleteAfterRun);
