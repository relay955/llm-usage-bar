using Microsoft.Win32;

namespace LLMUsageBar.Module;

public static class StartupManager {
    const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppRegistryValueName = "LLMUsageBar";

    /// <summary>
    /// 현재 설정에 따라 Windows 로그인 시 프로그램 자동 실행 등록을 동기화합니다.
    /// </summary>
    public static void Apply(bool runAtStartup) {
        using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(RunRegistryKeyPath, true);

        if (!runAtStartup) {
            runKey.DeleteValue(AppRegistryValueName, false);
            return;
        }

        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("실행 파일 경로를 확인할 수 없습니다.");
        runKey.SetValue(AppRegistryValueName, $"\"{executablePath}\"");
    }
}
