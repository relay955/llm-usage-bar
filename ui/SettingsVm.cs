using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

using LLMUsageBar.Module;
using LLMUsageBar.Util;

namespace LLMUsageBar.ui;

public class SettingsVm:INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;

    public int SelectedTabIndex { get; set; }
    public AppSettings PendingAppSettings { get; set; }
    public string CommandCodeMonthlyMaxCreditsText { get; set; }
    public string ValidationMessage { get; set; } = "";

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public SettingsVm() {
        PendingAppSettings = AppSettingsStore.Load();
        CommandCodeMonthlyMaxCreditsText = PendingAppSettings.CommandCodeMonthlyMaxCredits.ToString();

        SaveCommand = new Command(OnSave);
        CancelCommand = new Command(OnCancel);
    }

    void OnSave(object parameter) {
        if(PendingAppSettings.RefreshIntervalMinutes <= 0) {
            ValidationMessage = "자동갱신 주기는 1 이상의 숫자로 입력해주세요.";
            return;
        }

        string monthlyMaxCreditsText = CommandCodeMonthlyMaxCreditsText.Trim();
        double monthlyMaxCredits = 0;
        if (monthlyMaxCreditsText.Length > 0 && (!double.TryParse(monthlyMaxCreditsText, out monthlyMaxCredits)
            || !double.IsFinite(monthlyMaxCredits) || monthlyMaxCredits < 0)) {
            ValidationMessage = "Command Code 월간 최대 크레딧은 0 이상의 숫자로 입력해주세요.";
            return;
        }
        PendingAppSettings.CommandCodeMonthlyMaxCredits = monthlyMaxCredits;

        try {
            StartupManager.Apply(PendingAppSettings.RunAtStartup);
            AppSettingsStore.Save(PendingAppSettings);
        } catch (Exception exception) {
            ValidationMessage = $"자동 실행 설정을 저장하지 못했습니다: {exception.Message}";
            return;
        }

        App.Settings = PendingAppSettings;
        CloseWindow(parameter as Window, true);
    }

    void OnCancel(object parameter) => CloseWindow(parameter as Window, false);

    void CloseWindow(Window? window, bool dialogResult) {
        if (window is null) return;

        window.DialogResult = dialogResult;
        window.Close();
    }
}
