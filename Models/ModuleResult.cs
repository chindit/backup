namespace PlexBackup.Models;

public sealed record ModuleResult(
    string ModuleName,
    bool Success,
    string Message);
