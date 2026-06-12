using System.Windows;
using LLMUsageBar.Models;
using LLMUsageBar.Services;

namespace LLMUsageBar;

public partial class App : Application {
    public static AppSettings Settings { get; set; } = new();

    protected override void OnStartup(StartupEventArgs e) {
        Settings = AppSettingsStore.Load();

        base.OnStartup(e);
    }
}
