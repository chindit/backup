using System.Text.Json;

namespace PlexBackup.Resources;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static AppConfig Load(FileInfo configFile)
    {
        ArgumentNullException.ThrowIfNull(configFile);

        if (!configFile.Exists)
        {
            throw new FileNotFoundException(
                $"Configuration file not found: {configFile.FullName}",
                configFile.FullName);
        }

        try
        {
            AppConfig? config = JsonSerializer.Deserialize<AppConfig>(
                File.ReadAllText(configFile.FullName),
                SerializerOptions);
            return config ?? throw new InvalidDataException(
                $"Configuration file is empty: {configFile.FullName}");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Invalid configuration file {configFile.FullName}: {exception.Message}",
                exception);
        }
    }
}
