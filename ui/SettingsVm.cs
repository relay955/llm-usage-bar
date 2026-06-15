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
    public string ValidationMessage { get; set; } = "";

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public SettingsVm() {
        PendingAppSettings = AppSettingsStore.Load();

        SaveCommand = new Command(OnSave);
        CancelCommand = new Command(OnCancel);
    }

    void OnSave(object parameter) {
        if(PendingAppSettings.RefreshIntervalMinutes <= 0) {
            ValidationMessage = "자동갱신 주기는 1 이상의 숫자로 입력해주세요.";
            return;
        }

        AppSettingsStore.Save(PendingAppSettings);
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
