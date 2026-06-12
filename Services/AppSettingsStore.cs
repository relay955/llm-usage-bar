using System.IO;
using System.Text;
using LLMUsageBar.Models;
using Tomlyn;
using Tomlyn.Model;

namespace LLMUsageBar.Services;

public static class AppSettingsStore {
    private const string SettingsFileName = "settings.toml";
    private const string RefreshIntervalKey = "refresh_interval_minutes";

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
            RefreshIntervalMinutes = ReadPositiveInt(table, RefreshIntervalKey, AppSettings.DefaultRefreshIntervalMinutes)
        };
    }

    public static void Save(AppSettings settings) {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);

        var table = new TomlTable {
            [RefreshIntervalKey] = Math.Max(1, settings.RefreshIntervalMinutes)
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
}
