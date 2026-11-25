namespace PlexBackup.Resources;

public sealed class ModulesConfig
{
    public PlexModuleConfig? Plex { get; init; }
    public SiloModuleConfig? Silo { get; init; }
    public HomeAssistantModuleConfig? HomeAssistant { get; init; }

    public bool IsConfigured(string name) => name switch
    {
        "plex" => Plex is not null,
        "silo" => Silo is not null,
        "homeassistant" => HomeAssistant is not null,
        _ => false
    };

    public IReadOnlyList<string> ConfiguredNames()
    {
        var names = new List<string>(3);
        if (Plex is not null) names.Add("plex");
        if (Silo is not null) names.Add("silo");
        if (HomeAssistant is not null) names.Add("homeassistant");
        return names;
    }
}

public sealed class PlexModuleConfig
{
    public string SourceDirectory { get; init; } = "";
    public string TempDirectory { get; init; } = "";
    public string[] ExcludeDirectories { get; init; } = [];
}

public sealed class HomeAssistantModuleConfig
{
    public string Server { get; init; } = "";
    public string TokenCredential { get; init; } = "";
    public string TempDirectory { get; init; } = "";
}

public sealed class SiloModuleConfig
{
    public string Server { get; init; } = "";
    public string ApiKeyCredential { get; init; } = "";
    public string TempDirectory { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 3600;
    public int PollIntervalSeconds { get; init; } = 5;
}
