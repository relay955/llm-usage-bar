using System.Windows;
using System.Windows.Input;

using LLMUsageBar.Module;

namespace LLMUsageBar.ui;

public partial class Settings : Window {
    public Settings() {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings() {
        AppSettings settings = AppSettingsStore.Load();
        RefreshIntervalMinutesTextBox.Text = settings.RefreshIntervalMinutes.ToString();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) {
        if (!int.TryParse(RefreshIntervalMinutesTextBox.Text, out int refreshIntervalMinutes) ||
            refreshIntervalMinutes <= 0) {
            ValidationMessageTextBlock.Text = "자동갱신 주기는 1 이상의 숫자로 입력해주세요.";
            return;
        }

        var settings = new AppSettings {
            RefreshIntervalMinutes = refreshIntervalMinutes
        };

        AppSettingsStore.Save(settings);
        App.Settings = settings;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
        Close();
    }

    private void RefreshIntervalMinutesTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e) {
        e.Handled = !e.Text.All(char.IsDigit);
    }
}
