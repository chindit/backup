namespace PlexBackup.Resources;

public sealed class ModulesConfig
{
    public PlexModuleConfig? Plex { get; init; }
    public JellyfinModuleConfig? Jellyfin { get; init; }
    public HomeAssistantModuleConfig? HomeAssistant { get; init; }

    public bool IsConfigured(string name) => name switch
    {
        "plex" => Plex is not null,
        "jellyfin" => Jellyfin is not null,
        "homeassistant" => HomeAssistant is not null,
        _ => false
    };

    public IReadOnlyList<string> ConfiguredNames()
    {
        var names = new List<string>(3);
        if (Plex is not null) names.Add("plex");
        if (Jellyfin is not null) names.Add("jellyfin");
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

public sealed class JellyfinModuleConfig
{
    public string Server { get; init; } = "";
    public string ApiKeyCredential { get; init; } = "";
    public bool Metadata { get; init; } = true;
    public bool Subtitles { get; init; } = true;
    public bool Trickplay { get; init; }
}

public sealed class HomeAssistantModuleConfig
{
    public string Server { get; init; } = "";
    public string TokenCredential { get; init; } = "";
    public string TempDirectory { get; init; } = "";
}
