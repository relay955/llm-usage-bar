using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using LLMUsageBar.Module;
using LLMUsageBar.Provider;
using LLMUsageBar.ui;
using LLMUsageBar.Util;

namespace LLMUsageBar;

public enum UiMode {
    Loading,
    Balance,
    BalanceWithoutMax,
    DualQuota,
    SingleQouta
}

public class MainWindowVm:INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;

    readonly DispatcherTimer _refreshTimer = new();
    readonly DispatcherTimer _countdownTimer = new();
    readonly List<ILlmProvider> _providerList = new();
    DateTimeOffset? _shortResetAt;
    DateTimeOffset? _longResetAt;
    bool _isRefreshing;
    int _selectedProviderIndex;

    public string ProviderName { get; set; } = "";
    public string ProviderQuotaUrl { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public string CreditText { get; set; } = "";
    public double MaxBalance { get; set; } = 0;
    public double BalanceRatio { get; set; } = 0;
    public UiMode CurrentMode { get; set; } = UiMode.Loading;
    public double HourlyQuotaRatio { get; set; } = 0;
    public double WeeklyQuotaRatio { get; set; } = 0;
    public string ShortQuotaLabel { get; set; } = "hourly";
    public string LongQuotaLabel { get; set; } = "weekly";
    public string HourlyQuotaText { get; set; } = "";
    public string WeeklyQuotaText { get; set; } = "";
    public string HourlyRemainingTime { get; set; } = "";
    public string WeeklyRemainingTime { get; set; } = "";
    public double WindowWidth => string.IsNullOrEmpty(HourlyRemainingTime) && string.IsNullOrEmpty(WeeklyRemainingTime) ? 230 : 285;
    public bool HasMultipleProviders => this._providerList.Count > 1;

    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenProviderQuotaCommand { get; }
    public ICommand ManualRefreshCommand { get; }
    public ICommand PreviousProviderCommand { get; }
    public ICommand NextProviderCommand { get; }
    
    public MainWindowVm() {
        OpenSettingsCommand = new Command(async void (owner) => {
            await OpenSettingsAsync(owner as Window);
        });
        OpenProviderQuotaCommand = new Command(_ => {
            OpenProviderQuotaUrl();
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
        this._countdownTimer.Interval = TimeSpan.FromSeconds(1);
        this._countdownTimer.Tick -= CountdownTimer_Tick;
        this._countdownTimer.Tick += CountdownTimer_Tick;
        this._countdownTimer.Start();
        _ = RefreshCreditAsync();
    }

    /// <summary>
    /// 창이 닫히면 API 갱신 및 남은 시간 갱신 타이머를 중지합니다.
    /// </summary>
    public void StopTimer() {
        this._refreshTimer.Stop();
        this._countdownTimer.Stop();
    }

    /// <summary>
    /// API를 재호출하지 않고 매초 리셋까지 남은 시간을 갱신합니다.
    /// </summary>
    void CountdownTimer_Tick(object? sender, EventArgs e) => UpdateRemainingTimes();

    /// <summary>
    /// 두 윈도우의 남은 시간을 동일한 현재 시각으로 계산합니다.
    /// </summary>
    void UpdateRemainingTimes() {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HourlyRemainingTime = FormatRemainingTime(this._shortResetAt, now);
        WeeklyRemainingTime = FormatRemainingTime(this._longResetAt, now);
    }

    /// <summary>
    /// 24시간 미만은 시:분으로, 그 이상은 일과 시간으로 표시하며 만료 시 00:00으로 고정합니다.
    /// </summary>
    static string FormatRemainingTime(DateTimeOffset? resetAt, DateTimeOffset now) {
        if (!resetAt.HasValue) return "";

        TimeSpan remaining = resetAt.Value > now ? resetAt.Value - now : TimeSpan.Zero;
        return remaining.TotalHours >= 24
            ? $"{remaining.Days}d {remaining.Hours}h"
            : $"{remaining.Hours:00}:{remaining.Minutes:00}";
    }
    
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

        CurrentMode = UiMode.Loading;
        CreditText = "조회 중...";

        if (this._providerList.Count == 0) {
            ProviderName = "";
            ProviderQuotaUrl = "";
            ErrorMessage = "프로바이더 미사용";
            CreditText = "-";
            ClearUsageDisplay();
            CurrentMode = UiMode.BalanceWithoutMax;
            return;
        }

        this._isRefreshing = true;
        ErrorMessage = "";
        ClearUsageDisplay();

        try {
            var selectedProvider = this._providerList[this._selectedProviderIndex];
            ProviderName = selectedProvider.Name;
            ProviderQuotaUrl = selectedProvider.QuotaUrl;
            if (selectedProvider.HasShortQuota || selectedProvider.HasLongQuota) {
                var quota = await selectedProvider.GetCurrentQuotaAsync();
                this._shortResetAt = selectedProvider.HasShortQuota ? quota.ShortResetAt : quota.LongResetAt;
                this._longResetAt = selectedProvider.HasShortQuota && selectedProvider.HasLongQuota ? quota.LongResetAt : null;
                UpdateRemainingTimes();
                if (selectedProvider.HasShortQuota && selectedProvider.HasLongQuota) {
                    ShortQuotaLabel = selectedProvider.ShortQuotaLabel;
                    LongQuotaLabel = selectedProvider.LongQuotaLabel;
                    CreditText = $"{ShortQuotaLabel} {quota.Short:0.#}% / {LongQuotaLabel} {quota.Long:0.#}%";
                    CurrentMode = UiMode.DualQuota;
                    HourlyQuotaRatio = quota.Short / 100;
                    WeeklyQuotaRatio = quota.Long / 100;
                    HourlyQuotaText = $"{quota.Short:0.#}%";
                    WeeklyQuotaText = $"{quota.Long:0.#}%";
                } else {
                    // Single quota (e.g. Codex weekly) - display as gauge bar
                    string label = selectedProvider.HasShortQuota ? selectedProvider.ShortQuotaLabel : selectedProvider.LongQuotaLabel;
                    double ratio = selectedProvider.HasShortQuota ? quota.Short / 100.0 : quota.Long / 100.0;
                    ShortQuotaLabel = label;
                    HourlyQuotaRatio = ratio;
                    HourlyQuotaText = $"{ratio * 100:0.#}%";
                    CurrentMode = UiMode.SingleQouta;
                }
            } else {
                var balance = await selectedProvider.GetCurrentBalanceAsync(App.Settings);
                this.MaxBalance = balance.Max;
                this.BalanceRatio = balance.Max > 0 ? balance.Remain / balance.Max : 0;
                CreditText = $"${balance.Remain:0.00}";
                CurrentMode = balance.Max > 0 ? UiMode.Balance : UiMode.BalanceWithoutMax;
            }
        } catch(Exception e) {
            ErrorMessage = e.Message;
            CreditText = "-";
            ClearUsageDisplay();
            CurrentMode = UiMode.BalanceWithoutMax;
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

        SaveSelectedProvider();
        await RefreshCreditAsync();
    }

    void SaveSelectedProvider() {
        if (this._providerList.Count == 0) return;

        App.Settings.SelectedProviderName = this._providerList[this._selectedProviderIndex].Name;
        AppSettingsStore.Save(App.Settings);
    }

    void PrepareProviderList() {
        this._providerList.Clear();

        if (App.Settings.UseOpenRouter) this._providerList.Add(new OpenRouterProvider());
        if (App.Settings.UseNanoGpt) this._providerList.Add(new NanoGptProvider());
        if (App.Settings.UseChutes) this._providerList.Add(new ChutesProvider());
        if (App.Settings.UseCodex) this._providerList.Add(new CodexProvider());
        if (App.Settings.UseAntigravityClaude) this._providerList.Add(new AntigravityProvider(AntigravityModelGroup.ClaudeGpt));
        if (App.Settings.UseAntigravityGemini) this._providerList.Add(new AntigravityProvider(AntigravityModelGroup.Gemini));
        if (App.Settings.UseAntigravityFrontier) this._providerList.Add(new AntigravityProvider(AntigravityModelGroup.GeminiFrontier));
        if (App.Settings.UseOllamaCloud) this._providerList.Add(new OllamaCloudProvider());
        if (App.Settings.UseCommandCode) this._providerList.Add(new CommandCodeProvider());

        if (this._selectedProviderIndex < 0 || this._selectedProviderIndex >= this._providerList.Count) 
            this._selectedProviderIndex = 0;

        int savedProviderIndex = this._providerList.FindIndex(provider =>
            string.Equals(provider.Name, App.Settings.SelectedProviderName, StringComparison.Ordinal));
        if (savedProviderIndex >= 0) {
            this._selectedProviderIndex = savedProviderIndex;
        }

        ProviderName = this._providerList.Count == 0
            ? ""
            : this._providerList[this._selectedProviderIndex].Name;
        ProviderQuotaUrl = this._providerList.Count == 0
            ? ""
            : this._providerList[this._selectedProviderIndex].QuotaUrl;

        if (this._providerList.Count > 0) {
            SaveSelectedProvider();
        }
    }

    void OpenProviderQuotaUrl() {
        if (string.IsNullOrWhiteSpace(ProviderQuotaUrl)) return;

        try {
            Process.Start(new ProcessStartInfo(ProviderQuotaUrl) {
                UseShellExecute = true
            });
        } catch (Exception exception) {
            ErrorMessage = exception.Message;
        }
    }

    void ClearUsageDisplay() {
        MaxBalance = 0;
        BalanceRatio = 0;
        ShortQuotaLabel = "hourly";
        LongQuotaLabel = "weekly";
        HourlyQuotaRatio = 0;
        WeeklyQuotaRatio = 0;
        HourlyQuotaText = "";
        WeeklyQuotaText = "";
        this._shortResetAt = null;
        this._longResetAt = null;
        UpdateRemainingTimes();
    }
}
