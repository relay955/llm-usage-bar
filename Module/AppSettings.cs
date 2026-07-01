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
    public bool UseCodex { get; set; }
    public string CodexAuthJsonPath { get; set; } = AppSettingsStore.DefaultCodexAuthJsonPath;
    public bool UseOpenRouter { get; set; }
    public string OpenRouterApiKey { get; set; } = "";
    public bool UseChutes { get; set; }
    public string ChutesFingerprint { get; set; } = "";
    public double ChutesMaxBalance { get; set; }
    public double OpenRouterMaxBalance { get; set; }
    public bool UseOllamaCloud { get; set; }
    public string OllamaCloudSessionCookie { get; set; } = "";
}

public static class AppSettingsStore {
    private const string SettingsFileName = "settings.toml";
    private const string RefreshIntervalKey = "refresh_interval_minutes";
    private const string UseCodexKey = "useCodex";
    private const string CodexAuthJsonPathKey = "CodexAuthJsonPath";
    private const string UseOpenRouterKey = "useOpenRouter";
    private const string OpenRouterApiKeyKey = "OpenRouterApiKey";
    private const string UseChutesKey = "useChutes";
    private const string ChutesFingerprintKey = "ChutesFingerprint";
    private const string ChutesMaxBalanceKey = "ChutesMaxBalance";
    private const string OpenRouterMaxBalanceKey = "OpenRouterMaxBalance";
    private const string UseOllamaCloudKey = "useOllamaCloud";
    private const string OllamaCloudSessionCookieKey = "session_cookie";

    public static string SettingsFilePath {
        get {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataPath, "LLMUsageBar", SettingsFileName);
        }
    }

    public static string DefaultCodexAuthJsonPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");

    public static AppSettings Load() {
        if (!File.Exists(SettingsFilePath)) {
            return new AppSettings();
        }

        string content = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
        TomlTable table = TomlSerializer.Deserialize<TomlTable>(content) ?? new TomlTable();

        return new AppSettings {
            RefreshIntervalMinutes = ReadPositiveInt(table, RefreshIntervalKey, 10),
            UseCodex = ReadBool(table, UseCodexKey, false),
            CodexAuthJsonPath = ReadString(table, CodexAuthJsonPathKey, DefaultCodexAuthJsonPath),
            UseOpenRouter = ReadBool(table, UseOpenRouterKey, false),
            OpenRouterApiKey = ReadString(table, OpenRouterApiKeyKey, ""),
            UseChutes = ReadBool(table, UseChutesKey, false),
            ChutesFingerprint = ReadString(table, ChutesFingerprintKey, ""),
            ChutesMaxBalance = ReadDouble(table, ChutesMaxBalanceKey, 0),
            OpenRouterMaxBalance = ReadDouble(table, OpenRouterMaxBalanceKey, 0),
            UseOllamaCloud = ReadBool(table, UseOllamaCloudKey, false),
            OllamaCloudSessionCookie = ReadString(table, OllamaCloudSessionCookieKey, "")
        };
    }

    public static void Save(AppSettings settings) {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);

        var table = new TomlTable {
            [RefreshIntervalKey] = Math.Max(1, settings.RefreshIntervalMinutes),
            [UseCodexKey] = settings.UseCodex,
            [CodexAuthJsonPathKey] = settings.CodexAuthJsonPath,
            [UseOpenRouterKey] = settings.UseOpenRouter,
            [OpenRouterApiKeyKey] = settings.OpenRouterApiKey,
            [UseChutesKey] = settings.UseChutes,
            [ChutesFingerprintKey] = settings.ChutesFingerprint,
            [ChutesMaxBalanceKey] = settings.ChutesMaxBalance,
            [OpenRouterMaxBalanceKey] = settings.OpenRouterMaxBalance,
            [UseOllamaCloudKey] = settings.UseOllamaCloud,
            [OllamaCloudSessionCookieKey] = settings.OllamaCloudSessionCookie
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

    private static double ReadDouble(TomlTable table, string key, double defaultValue) {
        if (!table.TryGetValue(key, out object? value)) {
            return defaultValue;
        }

        return value switch {
            double doubleValue => doubleValue,
            float floatValue => (double)floatValue,
            int intValue => intValue,
            long longValue => longValue,
            string stringValue when double.TryParse(stringValue, out double parsed) => parsed,
            _ => defaultValue
        };
    }
}
