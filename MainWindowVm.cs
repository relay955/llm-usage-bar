using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using LLMUsageBar.Module;
using LLMUsageBar.Provider;
using LLMUsageBar.ui;
using LLMUsageBar.Util;

namespace LLMUsageBar;

public class MainWindowVm:INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;

    readonly DispatcherTimer _refreshTimer = new();
    readonly List<ILlmProvider> _providerList = new();
    bool _isRefreshing;
    int _selectedProviderIndex;

    public string ProviderName { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public string CreditText { get; set; } = "조회 중...";
    public bool HasMultipleProviders => this._providerList.Count > 1;

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
        var result = settingsWindow.ShowDialog();
        if (result != true) return;

        PrepareProviderList();
        ConfigureRefreshTimer();
        await RefreshCreditAsync();
    }


    async Task RefreshCreditAsync() {
        if (this._isRefreshing) return;

        if (this._providerList.Count == 0) {
            ProviderName = "";
            ErrorMessage = "프로바이더 미사용";
            CreditText = "-";
            return;
        }

        this._isRefreshing = true;
        ErrorMessage = "";

        try {
            var selectedProvider = this._providerList[this._selectedProviderIndex];
            ProviderName = selectedProvider.Name;
            if (selectedProvider.HasShortQuota && selectedProvider.HasLongQuota) {
                var quota = await selectedProvider.GetCurrentQuotaAsync();
                CreditText = $"5h {quota.Short:0.#}% / W {quota.Long:0.#}%";
            } else {
                var balance = await selectedProvider.GetCurrentBalanceAsync(App.Settings);
                CreditText = $"${balance.Remain:0.00}";
            }
        } catch(Exception e) {
            ErrorMessage = e.Message;
            CreditText = "-";
        } finally {
            this._isRefreshing = false;
        }
    }

    async Task ChangeProviderAsync(int direction) {
        if (this._isRefreshing) return;
        if (this._providerList.Count <= 1) return;

        this._selectedProviderIndex += direction;

        if (this._selectedProviderIndex < 0) {
            this._selectedProviderIndex = this._providerList.Count - 1;
        } else if (this._selectedProviderIndex >= this._providerList.Count) {
            this._selectedProviderIndex = 0;
        }

        await RefreshCreditAsync();
    }

    void PrepareProviderList() {
        this._providerList.Clear();

        if (App.Settings.UseOpenRouter) this._providerList.Add(new OpenRouterProvider());
        if (App.Settings.UseChutes) this._providerList.Add(new ChutesProvider());
        if (App.Settings.UseCodex) this._providerList.Add(new CodexProvider());

        if (this._selectedProviderIndex < 0 || this._selectedProviderIndex >= this._providerList.Count) 
            this._selectedProviderIndex = 0;

        ProviderName = this._providerList.Count == 0
            ? ""
            : this._providerList[this._selectedProviderIndex].Name;
    }
}
