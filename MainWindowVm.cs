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
    readonly List<ILlmProvider> ProviderList = new();
    bool _isRefreshing;
    int _selectedProviderIndex;

    public string CreditText { get; set; } = "조회 중...";

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

    public void Init() {
        PrepareProviderList();
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

    async void RefreshTimer_Tick(object? sender, EventArgs e) => await RefreshCreditAsync();

    async Task OpenSettingsAsync(Window? owner) {
        var settingsWindow = new Settings { Owner = owner };
        bool? result = settingsWindow.ShowDialog();
        if (result != true) return;

        PrepareProviderList();
        ConfigureRefreshTimer();
        await RefreshCreditAsync();
    }


    async Task RefreshCreditAsync() {
        if (this._isRefreshing) return;

        if (this.ProviderList.Count == 0) {
            CreditText = "프로바이더 미사용";
            return;
        }

        this._isRefreshing = true;

        try {
            CreditText = await GetProviderCreditTextAsync(this.ProviderList[this._selectedProviderIndex]); }
        finally {
            this._isRefreshing = false;
        }
    }

    async Task ChangeProviderAsync(int direction) {
        if (this._isRefreshing) return;
        if (this.ProviderList.Count <= 1) return;

        this._selectedProviderIndex += direction;

        if (this._selectedProviderIndex < 0) {
            this._selectedProviderIndex = this.ProviderList.Count - 1;
        } else if (this._selectedProviderIndex >= this.ProviderList.Count) {
            this._selectedProviderIndex = 0;
        }

        await RefreshCreditAsync();
    }

    void PrepareProviderList() {
        this.ProviderList.Clear();

        if (App.Settings.UseOpenRouter) this.ProviderList.Add(new OpenRouterProvider());
        if (App.Settings.UseChutes) this.ProviderList.Add(new ChutesProvider());

        if (this._selectedProviderIndex < 0 || this._selectedProviderIndex >= this.ProviderList.Count) 
            this._selectedProviderIndex = 0;
    }

    async Task<string> GetProviderCreditTextAsync(ILlmProvider provider) {
        return provider switch {
            OpenRouterProvider => await GetOpenRouterCreditTextAsync(provider),
            ChutesProvider => await GetChutesCreditTextAsync(provider),
            _ => "프로바이더 오류"
        };
    }

    async Task<string> GetOpenRouterCreditTextAsync(ILlmProvider provider) {
        var balance = await provider.GetCurrentBalanceAsync(App.Settings);
        return $"OpenRouter ${balance.Remain:0.00}";
    }

    async Task<string> GetChutesCreditTextAsync(ILlmProvider provider) {
        var balance = await provider.GetCurrentBalanceAsync(App.Settings);
        return $"Chutes ${balance.Remain:0.00}";
    }
}
