using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LLMUsageBar.Provider;
using LLMUsageBar.ui;
using LLMUsageBar.Util;

namespace LLMUsageBar;

public class MainWindowVm:INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;

    readonly DispatcherTimer _refreshTimer = new();
    bool _isRefreshing;
    public string CreditText { get; set; } = "OpenRouter 조회 중...";
    public ICommand OpenSettingsCommand { get; }
    
    public MainWindowVm() {
        OpenSettingsCommand = new Command(async void (owner) => {
            await OpenSettingsAsync(owner as Window);
        });
    }
    public void StartTimer() {
        ConfigureRefreshTimer();
        _ = RefreshCreditAsync();
    }

    public void StopTimer() => this._refreshTimer.Stop();
    
    void ConfigureRefreshTimer() {
        this._refreshTimer.Stop();
        this._refreshTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, App.Settings.RefreshIntervalMinutes));
        this._refreshTimer.Tick -= RefreshTimer_Tick;
        this._refreshTimer.Tick += RefreshTimer_Tick;
        this._refreshTimer.Start();
    }

    async void RefreshTimer_Tick(object? sender, EventArgs e) {
        await RefreshCreditAsync();
    }
    
    async Task OpenSettingsAsync(Window? owner) {
        var settingsWindow = new Settings { Owner = owner };
        bool? result = settingsWindow.ShowDialog();

        if(result == true) {
            ConfigureRefreshTimer();
            await RefreshCreditAsync();
        }
    }


    async Task RefreshCreditAsync() {
        if (this._isRefreshing) return;

        if (!App.Settings.UseOpenRouter) {
            CreditText = "OpenRouter 미사용";
            return;
        }

        if (string.IsNullOrWhiteSpace(App.Settings.OpenRouterApiKey)) {
            CreditText = "OpenRouter API 키 필요";
            return;
        }

        this._isRefreshing = true;

        try {
            var provider = new OpenRouterProvider(App.Settings.OpenRouterApiKey);
            ILlmProvider.Balance balance = await provider.GetCurrentBalanceAsync();
            CreditText = $"OpenRouter ${balance.Remain:0.00}";
        }
        catch {
            CreditText = "OpenRouter 조회 실패";
        }
        finally {
            this._isRefreshing = false;
        }
    }
}
