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
    int _selectedProviderIndex;

    public string CreditText { get; set; } = "조회 중...";
    public bool HasMultipleProviders { get; set; }

    public ICommand OpenSettingsCommand { get; }
    public ICommand PreviousProviderCommand { get; }
    public ICommand NextProviderCommand { get; }
    
    public MainWindowVm() {
        OpenSettingsCommand = new Command(async void (owner) => {
            await OpenSettingsAsync(owner as Window);
        });
        PreviousProviderCommand = new Command(async void (_) => {
            await ChangeProviderAsync(-1);
        });
        NextProviderCommand = new Command(async void (_) => {
            await ChangeProviderAsync(1);
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

        var providerNames = GetEnabledProviderNames();
        HasMultipleProviders = providerNames.Count > 1;

        if (providerNames.Count == 0) {
            CreditText = "프로바이더 미사용";
            return;
        }

        EnsureSelectedProviderIndex(providerNames);
        this._isRefreshing = true;

        try {
            CreditText = await GetProviderCreditTextAsync(providerNames[this._selectedProviderIndex]);
        }
        finally {
            this._isRefreshing = false;
        }
    }

    async Task ChangeProviderAsync(int direction) {
        if (this._isRefreshing) return;

        var providerNames = GetEnabledProviderNames();
        HasMultipleProviders = providerNames.Count > 1;

        if (providerNames.Count <= 1) return;

        this._selectedProviderIndex += direction;

        if (this._selectedProviderIndex < 0) {
            this._selectedProviderIndex = providerNames.Count - 1;
        }
        else if (this._selectedProviderIndex >= providerNames.Count) {
            this._selectedProviderIndex = 0;
        }

        await RefreshCreditAsync();
    }

    static List<string> GetEnabledProviderNames() {
        List<string> providerNames = new();

        if (App.Settings.UseOpenRouter) {
            providerNames.Add("OpenRouter");
        }

        if (App.Settings.UseChutes) {
            providerNames.Add("Chutes");
        }

        return providerNames;
    }

    void EnsureSelectedProviderIndex(List<string> providerNames) {
        if (this._selectedProviderIndex < 0 || this._selectedProviderIndex >= providerNames.Count) {
            this._selectedProviderIndex = 0;
        }
    }

    async Task<string> GetProviderCreditTextAsync(string providerName) {
        return providerName switch {
            "OpenRouter" => await GetOpenRouterCreditTextAsync(),
            "Chutes" => await GetChutesCreditTextAsync(),
            _ => "프로바이더 오류"
        };
    }

    async Task<string> GetOpenRouterCreditTextAsync() {
        if (string.IsNullOrWhiteSpace(App.Settings.OpenRouterApiKey)) {
            return "OpenRouter API 키 필요";
        }

        try {
            var provider = new OpenRouterProvider();
            ILlmProvider.Balance balance = await provider.GetCurrentBalanceAsync(App.Settings);
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
            var provider = new ChutesProvider();
            ILlmProvider.Balance balance = await provider.GetCurrentBalanceAsync(App.Settings);
            return $"Chutes ${balance.Remain:0.00}";
        }
        catch {
            return "Chutes 조회 실패";
        }
    }
}
