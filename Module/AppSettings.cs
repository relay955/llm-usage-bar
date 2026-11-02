using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

using Tomlyn;
using Tomlyn.Model;

namespace LLMUsageBar.Module;

public sealed class AppSettings:INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    public int RefreshIntervalMinutes { get; set; } = 60;
    public bool UseAdaptiveRefresh { get; set; } = true;
    public bool RunAtStartup { get; set; }
    public bool UseCodex { get; set; }
    public string CodexAuthJsonPath { get; set; } = AppSettingsStore.DefaultCodexAuthJsonPath;
    public bool UseAntigravityClaude { get; set; }
    public bool UseAntigravityGemini { get; set; }
    public bool UseAntigravityFrontier { get; set; }
    public bool UseOpenRouter { get; set; }
    public string OpenRouterApiKey { get; set; } = "";
    public bool UseNanoGpt { get; set; }
    public string NanoGptApiKey { get; set; } = "";
    public double NanoGptMaxBalance { get; set; }
    public bool UseChutes { get; set; }
    public string ChutesFingerprint { get; set; } = "";
    public double ChutesMaxBalance { get; set; }
    public double OpenRouterMaxBalance { get; set; }
    public bool UseOllamaCloud { get; set; }
    public string OllamaCloudSessionCookie { get; set; } = "";
    public bool UseCommandCode { get; set; }
    public string CommandCodeAuthJsonPath { get; set; } = AppSettingsStore.DefaultCommandCodeAuthJsonPath;
    public string SelectedProviderName { get; set; } = "";
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
}

public static class AppSettingsStore {
    private const string SettingsFileName = "settings.toml";
    private const string RefreshIntervalKey = "refresh_interval_minutes";
    const string UseAdaptiveRefreshKey = "use_adaptive_refresh";
    private const string RunAtStartupKey = "run_at_startup";
    private const string UseCodexKey = "useCodex";
    private const string CodexAuthJsonPathKey = "CodexAuthJsonPath";
    private const string UseAntigravityKey = "useAntigravity";
    const string UseAntigravityClaudeKey = "useAntigravityClaude";
    const string UseAntigravityGeminiKey = "useAntigravityGemini";
    const string UseAntigravityFrontierKey = "useAntigravityFrontier";
    private const string UseOpenRouterKey = "useOpenRouter";
    private const string OpenRouterApiKeyKey = "OpenRouterApiKey";
    private const string UseNanoGptKey = "useNanoGpt";
    private const string NanoGptApiKeyKey = "NanoGptApiKey";
    private const string NanoGptMaxBalanceKey = "NanoGptMaxBalance";
    private const string UseChutesKey = "useChutes";
    private const string ChutesFingerprintKey = "ChutesFingerprint";
    private const string ChutesMaxBalanceKey = "ChutesMaxBalance";
    private const string OpenRouterMaxBalanceKey = "OpenRouterMaxBalance";
    private const string UseOllamaCloudKey = "useOllamaCloud";
    private const string OllamaCloudSessionCookieKey = "session_cookie";
    private const string UseCommandCodeKey = "useCommandCode";
    private const string CommandCodeAuthJsonPathKey = "CommandCodeAuthJsonPath";
    private const string SelectedProviderNameKey = "selected_provider_name";
    private const string WindowLeftKey = "window_left";
    private const string WindowTopKey = "window_top";

    public static string SettingsFilePath {
        get {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataPath, "LLMUsageBar", SettingsFileName);
        }
    }

    public static string DefaultCodexAuthJsonPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");

    public static string DefaultCommandCodeAuthJsonPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".commandcode", "auth.json");

    public static AppSettings Load() {
        if (!File.Exists(SettingsFilePath)) {
            return new AppSettings();
        }

        string content = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
        TomlTable table = TomlSerializer.Deserialize<TomlTable>(content) ?? new TomlTable();

        return new AppSettings {
            RefreshIntervalMinutes = ReadPositiveInt(table, RefreshIntervalKey, 60),
            UseAdaptiveRefresh = ReadBool(table, UseAdaptiveRefreshKey, true),
            RunAtStartup = ReadBool(table, RunAtStartupKey, false),
            UseCodex = ReadBool(table, UseCodexKey, false),
            CodexAuthJsonPath = ReadString(table, CodexAuthJsonPathKey, DefaultCodexAuthJsonPath),
            UseAntigravityClaude = ReadBool(table, UseAntigravityClaudeKey, ReadBool(table, UseAntigravityKey, false)),
            UseAntigravityGemini = ReadBool(table, UseAntigravityGeminiKey, ReadBool(table, UseAntigravityKey, false)),
            UseAntigravityFrontier = ReadBool(table, UseAntigravityFrontierKey, ReadBool(table, UseAntigravityKey, false)),
            UseOpenRouter = ReadBool(table, UseOpenRouterKey, false),
            OpenRouterApiKey = ReadString(table, OpenRouterApiKeyKey, ""),
            UseNanoGpt = ReadBool(table, UseNanoGptKey, false),
            NanoGptApiKey = ReadString(table, NanoGptApiKeyKey, ""),
            NanoGptMaxBalance = ReadDouble(table, NanoGptMaxBalanceKey, 0),
            UseChutes = ReadBool(table, UseChutesKey, false),
            ChutesFingerprint = ReadString(table, ChutesFingerprintKey, ""),
            ChutesMaxBalance = ReadDouble(table, ChutesMaxBalanceKey, 0),
            OpenRouterMaxBalance = ReadDouble(table, OpenRouterMaxBalanceKey, 0),
            UseOllamaCloud = ReadBool(table, UseOllamaCloudKey, false),
            OllamaCloudSessionCookie = ReadString(table, OllamaCloudSessionCookieKey, ""),
            UseCommandCode = ReadBool(table, UseCommandCodeKey, false),
            CommandCodeAuthJsonPath = ReadString(table, CommandCodeAuthJsonPathKey, DefaultCommandCodeAuthJsonPath),
            SelectedProviderName = ReadString(table, SelectedProviderNameKey, ""),
            WindowLeft = ReadNullableDouble(table, WindowLeftKey),
            WindowTop = ReadNullableDouble(table, WindowTopKey)
        };
    }

    public static void Save(AppSettings settings) {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);

        var table = new TomlTable {
            [RefreshIntervalKey] = Math.Max(1, settings.RefreshIntervalMinutes),
            [UseAdaptiveRefreshKey] = settings.UseAdaptiveRefresh,
            [RunAtStartupKey] = settings.RunAtStartup,
            [UseCodexKey] = settings.UseCodex,
            [CodexAuthJsonPathKey] = settings.CodexAuthJsonPath,
            [UseAntigravityClaudeKey] = settings.UseAntigravityClaude,
            [UseAntigravityGeminiKey] = settings.UseAntigravityGemini,
            [UseAntigravityFrontierKey] = settings.UseAntigravityFrontier,
            [UseOpenRouterKey] = settings.UseOpenRouter,
            [OpenRouterApiKeyKey] = settings.OpenRouterApiKey,
            [UseNanoGptKey] = settings.UseNanoGpt,
            [NanoGptApiKeyKey] = settings.NanoGptApiKey,
            [NanoGptMaxBalanceKey] = settings.NanoGptMaxBalance,
            [UseChutesKey] = settings.UseChutes,
            [ChutesFingerprintKey] = settings.ChutesFingerprint,
            [ChutesMaxBalanceKey] = settings.ChutesMaxBalance,
            [OpenRouterMaxBalanceKey] = settings.OpenRouterMaxBalance,
            [UseOllamaCloudKey] = settings.UseOllamaCloud,
            [OllamaCloudSessionCookieKey] = settings.OllamaCloudSessionCookie,
            [UseCommandCodeKey] = settings.UseCommandCode,
            [CommandCodeAuthJsonPathKey] = settings.CommandCodeAuthJsonPath,
            [SelectedProviderNameKey] = settings.SelectedProviderName
        };

        if (settings.WindowLeft.HasValue) table[WindowLeftKey] = settings.WindowLeft.Value;
        if (settings.WindowTop.HasValue) table[WindowTopKey] = settings.WindowTop.Value;

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

    static double? ReadNullableDouble(TomlTable table, string key) {
        if (!table.TryGetValue(key, out object? value)) {
            return null;
        }

        return value switch {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            int intValue => intValue,
            long longValue => longValue,
            string stringValue when double.TryParse(stringValue, out double parsed) => parsed,
            _ => null
        };
    }
}
