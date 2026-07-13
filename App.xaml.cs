using System.Windows;

using LLMUsageBar.Module;

namespace LLMUsageBar;

public partial class App : Application {
    public static AppSettings Settings { get; set; } = new();

    protected override void OnStartup(StartupEventArgs e) {
        Settings = AppSettingsStore.Load();

        try {
            StartupManager.Apply(Settings.RunAtStartup);
        } catch {
            // 자동 실행 동기화 실패가 프로그램 실행을 막지 않도록 합니다.
        }

        base.OnStartup(e);
    }
}
