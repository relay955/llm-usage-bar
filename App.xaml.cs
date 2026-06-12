using System.Windows;

using LLMUsageBar.Module;

namespace LLMUsageBar;

public partial class App : Application {
    public static AppSettings Settings { get; set; } = new();

    protected override void OnStartup(StartupEventArgs e) {
        Settings = AppSettingsStore.Load();

        base.OnStartup(e);
    }
}
