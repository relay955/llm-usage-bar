using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

using Tomlyn;
using Tomlyn.Model;

namespace LLMUsageBar.Module;

public sealed class AppSettings:INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    public int RefreshIntervalMinutes { get; set; }
    public bool UseOpenRouter { get; set; }
    public string OpenRouterApiKey { get; set; } = "";
    public bool UseChutes { get; set; }
    public string ChutesApiKey { get; set; } = "";
    public string ChutesUserIdOrUsername { get; set; } = "";
}

public static class AppSettingsStore {
    private const string SettingsFileName = "settings.toml";
    private const string RefreshIntervalKey = "refresh_interval_minutes";
    private const string UseOpenRouterKey = "useOpenRouter";
    private const string OpenRouterApiKeyKey = "OpenRouterApiKey";
    private const string UseChutesKey = "useChutes";
    private const string ChutesApiKeyKey = "ChutesApiKey";
    private const string ChutesUserIdOrUsernameKey = "ChutesUserIdOrUsername";

    public static string SettingsFilePath {
        get {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataPath, "LLMUsageBar", SettingsFileName);
        }
    }

    public static AppSettings Load() {
        if (!File.Exists(SettingsFilePath)) {
            return new AppSettings();
        }

        string content = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
        TomlTable table = TomlSerializer.Deserialize<TomlTable>(content) ?? new TomlTable();

        return new AppSettings {
            RefreshIntervalMinutes = ReadPositiveInt(table, RefreshIntervalKey, 10),
            UseOpenRouter = ReadBool(table, UseOpenRouterKey, false),
            OpenRouterApiKey = ReadString(table, OpenRouterApiKeyKey, ""),
            UseChutes = ReadBool(table, UseChutesKey, false),
            ChutesApiKey = ReadString(table, ChutesApiKeyKey, ""),
            ChutesUserIdOrUsername = ReadString(table, ChutesUserIdOrUsernameKey, "")
        };
    }

    public static void Save(AppSettings settings) {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);

        var table = new TomlTable {
            [RefreshIntervalKey] = Math.Max(1, settings.RefreshIntervalMinutes),
            [UseOpenRouterKey] = settings.UseOpenRouter,
            [OpenRouterApiKeyKey] = settings.OpenRouterApiKey,
            [UseChutesKey] = settings.UseChutes,
            [ChutesApiKeyKey] = settings.ChutesApiKey,
            [ChutesUserIdOrUsernameKey] = settings.ChutesUserIdOrUsername
        };

        File.WriteAllText(SettingsFilePath, TomlSerializer.Serialize(table), Encoding.UTF8);
    }

    private static int ReadPositiveInt(TomlTable table, string key, int defaultValue) {
        if (!table.TryGetValue(key, out object? value)) {
            return defaultValue;
        }

        int result = value switch {
            int intValue => intValue,
            long longValue when longValue <= int.MaxValue => (int)longValue,
            _ => defaultValue
        };

        return result > 0 ? result : defaultValue;
    }

    private static bool ReadBool(TomlTable table, string key, bool defaultValue) {
        if (!table.TryGetValue(key, out object? value)) {
            return defaultValue;
        }

        return value is bool boolValue ? boolValue : defaultValue;
    }

    private static string ReadString(TomlTable table, string key, string defaultValue) {
        if (!table.TryGetValue(key, out object? value)) {
            return defaultValue;
        }

        return value is string stringValue ? stringValue : defaultValue;
    }
}
