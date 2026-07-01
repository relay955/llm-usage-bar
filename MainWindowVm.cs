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
    public double MaxBalance { get; set; } = 0;
    public double BalanceRatio { get; set; } = 0;
    public bool HasBalanceDisplay { get; set; }
    public bool HasDualQuota { get; set; }
    public bool ShowCreditText { get; set; } = true;
    public double HourlyQuotaRatio { get; set; } = 0;
    public double WeeklyQuotaRatio { get; set; } = 0;
    public string ShortQuotaLabel { get; set; } = "hourly";
    public string LongQuotaLabel { get; set; } = "weekly";
    public string HourlyQuotaText { get; set; } = "";
    public string WeeklyQuotaText { get; set; } = "";
    public bool HasMultipleProviders => this._providerList.Count > 1;

    public ICommand OpenSettingsCommand { get; }
    public ICommand ManualRefreshCommand { get; }
    public ICommand PreviousProviderCommand { get; }
    public ICommand NextProviderCommand { get; }
    
    public MainWindowVm() {
        OpenSettingsCommand = new Command(async void (owner) => {
            await OpenSettingsAsync(owner as Window);
        });
        ManualRefreshCommand = new Command(async void (_) => {
            await RefreshCreditAsync();
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
            ClearUsageDisplay();
            ShowCreditText = true;
            return;
        }

        this._isRefreshing = true;
        ErrorMessage = "";
        ClearUsageDisplay();

        try {
            var selectedProvider = this._providerList[this._selectedProviderIndex];
            ProviderName = selectedProvider.Name;
            if (selectedProvider.HasShortQuota || selectedProvider.HasLongQuota) {
                var quota = await selectedProvider.GetCurrentQuotaAsync();
                if (selectedProvider.HasShortQuota && selectedProvider.HasLongQuota) {
                    ShortQuotaLabel = selectedProvider.ShortQuotaLabel;
                    LongQuotaLabel = selectedProvider.LongQuotaLabel;
                    CreditText = $"{ShortQuotaLabel} {quota.Short:0.#}% / {LongQuotaLabel} {quota.Long:0.#}%";
                    HasDualQuota = true;
                    ShowCreditText = false;
                    HourlyQuotaRatio = quota.Short / 100;
                    WeeklyQuotaRatio = quota.Long / 100;
                    HourlyQuotaText = $"{quota.Short:0.#}%";
                    WeeklyQuotaText = $"{quota.Long:0.#}%";
                } else if (selectedProvider.HasShortQuota) {
                    CreditText = $"{quota.Short:0.#}%";
                    ShowCreditText = true;
                } else {
                    CreditText = $"{quota.Long:0.#}%";
                    ShowCreditText = true;
                }
            } else {
                var balance = await selectedProvider.GetCurrentBalanceAsync(App.Settings);
                this.MaxBalance = balance.Max;
                this.BalanceRatio = balance.Max > 0 ? balance.Remain / balance.Max : 0;
                CreditText = $"${balance.Remain:0.00}";
                HasBalanceDisplay = balance.Max > 0;
                ShowCreditText = !HasBalanceDisplay;
            }
        } catch(Exception e) {
            ErrorMessage = e.Message;
            CreditText = "-";
            ClearUsageDisplay();
            ShowCreditText = true;
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
        if (App.Settings.UseOllamaCloud) this._providerList.Add(new OllamaCloudProvider());

        if (this._selectedProviderIndex < 0 || this._selectedProviderIndex >= this._providerList.Count) 
            this._selectedProviderIndex = 0;

        ProviderName = this._providerList.Count == 0
            ? ""
            : this._providerList[this._selectedProviderIndex].Name;
    }

    void ClearUsageDisplay() {
        MaxBalance = 0;
        BalanceRatio = 0;
        HasBalanceDisplay = false;
        HasDualQuota = false;
        ShortQuotaLabel = "hourly";
        LongQuotaLabel = "weekly";
        HourlyQuotaRatio = 0;
        WeeklyQuotaRatio = 0;
        HourlyQuotaText = "";
        WeeklyQuotaText = "";
    }
}
