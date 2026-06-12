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
    public string CreditText { get; set; } = "조회 중...";
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

        if (!App.Settings.UseOpenRouter && !App.Settings.UseChutes) {
            CreditText = "프로바이더 미사용";
            return;
        }

        this._isRefreshing = true;

        try {
            List<string> creditParts = new();

            if (App.Settings.UseOpenRouter) {
                creditParts.Add(await GetOpenRouterCreditTextAsync());
            }

            if (App.Settings.UseChutes) {
                creditParts.Add(await GetChutesCreditTextAsync());
            }

            CreditText = string.Join(" | ", creditParts);
        }
        finally {
            this._isRefreshing = false;
        }
    }

    async Task<string> GetOpenRouterCreditTextAsync() {
        if (string.IsNullOrWhiteSpace(App.Settings.OpenRouterApiKey)) {
            return "OpenRouter API 키 필요";
        }

        try {
            var provider = new OpenRouterProvider(App.Settings.OpenRouterApiKey);
            ILlmProvider.Balance balance = await provider.GetCurrentBalanceAsync();
            return $"OpenRouter ${balance.Remain:0.00}";
        }
        catch {
            return "OpenRouter 조회 실패";
        }
    }

    async Task<string> GetChutesCreditTextAsync() {
        if (string.IsNullOrWhiteSpace(App.Settings.ChutesApiKey)) {
            return "Chutes API 키 필요";
        }

        if (string.IsNullOrWhiteSpace(App.Settings.ChutesUserIdOrUsername)) {
            return "Chutes 사용자 필요";
        }

        try {
            var provider = new ChutesProvider(App.Settings.ChutesApiKey, App.Settings.ChutesUserIdOrUsername);
            ILlmProvider.Balance balance = await provider.GetCurrentBalanceAsync();
            return $"Chutes ${balance.Remain:0.00}";
        }
        catch {
            return "Chutes 조회 실패";
        }
    }
}
