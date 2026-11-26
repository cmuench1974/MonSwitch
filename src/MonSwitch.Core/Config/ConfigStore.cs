using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonSwitch.Core.Config;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppConfig))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;

/// <summary>Lädt und speichert die Konfiguration unter %APPDATA%\MonSwitch\config.json.</summary>
public static class ConfigStore
{
    /// <summary>Umgebungsvariable zum Umlenken des Konfigurationsordners (Tests, portable Nutzung).</summary>
    public const string ConfigDirEnvVar = "MONSWITCH_CONFIG_DIR";

    public static string Directory { get; } = ResolveDirectory();

    public static string FilePath => Path.Combine(Directory, "config.json");

    public static bool Exists => File.Exists(FilePath);

    private static string ResolveDirectory()
    {
        string? overrideDir = Environment.GetEnvironmentVariable(ConfigDirEnvVar);
        if (!string.IsNullOrWhiteSpace(overrideDir))
            return Path.GetFullPath(overrideDir);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonSwitch");
    }

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppConfig();
            return Deserialize(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Error($"Konfiguration konnte nicht gelesen werden: {ex.Message}");
            TryBackupBrokenFile();
            return new AppConfig();
        }
    }

    /// <summary>Speichert atomar (temporäre Datei + Ersetzen), damit nie eine halbe Datei entsteht.</summary>
    public static void Save(AppConfig config)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, Serialize(config));
        File.Move(tmp, FilePath, overwrite: true);
    }

    internal static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);

    internal static AppConfig Deserialize(string json)
    {
        var config = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig) ?? new AppConfig();
        config.Bindings ??= [];
        config.MonitorCache ??= [];
        foreach (var b in config.Bindings)
            b.Actions ??= [];
        return config;
    }

    private static void TryBackupBrokenFile()
    {
        try
        {
            File.Copy(FilePath, FilePath + ".defekt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
