using System.Diagnostics;
using System.Globalization;
using System.ComponentModel;
using System.Collections.ObjectModel;
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
    string? _usageProviderName;
    int _selectedProviderIndex;
    string? _pendingProviderName;
    bool _refreshRequested;
    bool _refreshTimerEnabled;
    int _adaptiveRefreshIntervalMinutes = 1;
    (string ProviderName, double? Short, double? Long, double? Third, double? Balance)? _lastUsage;

    public ObservableCollection<ProviderListItem> AvailableProviders { get; } = new();

    public string ProviderName { get; set; } = "";
    public string ProviderQuotaUrl { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public bool IsRefreshing { get; set; }
    public bool HasStatusMessage => IsRefreshing || !string.IsNullOrEmpty(ErrorMessage);
    public string StatusMessage => IsRefreshing ? "Refreshing.." : !string.IsNullOrEmpty(ErrorMessage) ? ErrorMessage : ThirdQuotaStatusText;
    public string StatusToolTip => HasStatusMessage ? StatusMessage : ThirdQuotaLabel;
    public double? ThirdQuotaValue { get; set; }
    public double? ThirdQuotaMax { get; set; }
    public string ThirdQuotaLabel { get; set; } = "";
    public string ThirdQuotaFormat { get; set; } = "{0:0.#}%";
    public string ThirdQuotaStatusText => ThirdQuotaValue is double value
        ? ThirdQuotaMax is > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.#}%", Math.Clamp(value / ThirdQuotaMax.Value * 100, 0, 100))
            : string.Format(CultureInfo.InvariantCulture, ThirdQuotaFormat, value)
        : "";
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
    public ICommand SelectProviderCommand { get; }
    
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
        SelectProviderCommand = new Command(async void (parameter) => {
            if (parameter is ProviderListItem item) await SelectProviderAsync(item.Name);
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
        this._refreshTimerEnabled = false;
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
    
    /// <summary>
    /// 설정에 맞춰 갱신 간격을 초기화하고 자동갱신을 시작합니다.
    /// </summary>
    void ConfigureRefreshTimer() {
        this._refreshTimerEnabled = true;
        this._adaptiveRefreshIntervalMinutes = 1;
        this._lastUsage = null;
        this._refreshTimer.Tick -= RefreshTimer_Tick;
        this._refreshTimer.Tick += RefreshTimer_Tick;
        ScheduleNextRefresh();
    }

    /// <summary>
    /// 조회 완료 시점부터 다음 갱신을 예약하며 설정된 최대 주기를 넘지 않게 합니다.
    /// </summary>
    void ScheduleNextRefresh() {
        this._refreshTimer.Stop();
        if (!this._refreshTimerEnabled) return;

        int maximumMinutes = Math.Max(1, App.Settings.RefreshIntervalMinutes);
        int intervalMinutes = App.Settings.UseAdaptiveRefresh
            ? Math.Min(this._adaptiveRefreshIntervalMinutes, maximumMinutes)
            : maximumMinutes;
        this._refreshTimer.Interval = TimeSpan.FromMinutes(intervalMinutes);
        this._refreshTimer.Start();
    }

    /// <summary>
    /// 원본 사용량이 바뀌면 1분으로 되돌리고 같으면 최대 주기까지 간격을 두 배로 늘립니다.
    /// </summary>
    void UpdateAdaptiveRefreshInterval(string providerName, double? shortUsage, double? longUsage, double? thirdUsage, double? balance) {
        var usage = (ProviderName: providerName, Short: shortUsage, Long: longUsage, Third: thirdUsage, Balance: balance);
        this._adaptiveRefreshIntervalMinutes = this._lastUsage.HasValue && this._lastUsage.Value.Equals(usage)
            ? (int)Math.Min((long)this._adaptiveRefreshIntervalMinutes * 2, Math.Max(1, App.Settings.RefreshIntervalMinutes))
            : 1;
        this._lastUsage = usage;
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
        if (IsRefreshing) {
            this._refreshRequested = true;
            return;
        }

        this._refreshRequested = false;
        this._refreshTimer.Stop();

        if (this._providerList.Count == 0) {
            this._usageProviderName = null;
            ProviderName = "";
            ProviderQuotaUrl = "";
            ErrorMessage = "프로바이더 미사용";
            CreditText = "-";
            ClearUsageDisplay();
            CurrentMode = UiMode.BalanceWithoutMax;
            return;
        }

        IsRefreshing = true;
        ErrorMessage = "";

        try {
            var selectedProvider = this._providerList[this._selectedProviderIndex];
            ProviderName = selectedProvider.Name;
            ProviderQuotaUrl = selectedProvider.QuotaUrl;
            if (this._usageProviderName != selectedProvider.Name) {
                InitializeUsageDisplay(selectedProvider);
                this._usageProviderName = selectedProvider.Name;
            }
            if (selectedProvider.HasShortQuota || selectedProvider.HasLongQuota) {
                var quota = await selectedProvider.GetCurrentQuotaAsync();
                this._shortResetAt = selectedProvider.HasShortQuota ? quota.ShortResetAt : quota.LongResetAt;
                this._longResetAt = selectedProvider.HasShortQuota && selectedProvider.HasLongQuota ? quota.LongResetAt : null;
                ThirdQuotaValue = selectedProvider.HasThirdQuota ? quota.Third : null;
                ThirdQuotaMax = selectedProvider.HasThirdQuota ? quota.ThirdMax : null;
                ThirdQuotaLabel = selectedProvider.HasThirdQuota ? selectedProvider.ThirdQuotaLabel : "";
                ThirdQuotaFormat = selectedProvider.ThirdQuotaFormat;
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
                UpdateAdaptiveRefreshInterval(selectedProvider.Name,
                    selectedProvider.HasShortQuota ? quota.Short : null,
                    selectedProvider.HasLongQuota ? quota.Long : null,
                    selectedProvider.HasThirdQuota ? quota.Third : null, null);
            } else {
                var balance = await selectedProvider.GetCurrentBalanceAsync(App.Settings);
                this.MaxBalance = balance.Max;
                this.BalanceRatio = balance.Max > 0 ? balance.Remain / balance.Max : 0;
                CreditText = $"${balance.Remain:0.00}";
                CurrentMode = balance.Max > 0 ? UiMode.Balance : UiMode.BalanceWithoutMax;
                UpdateAdaptiveRefreshInterval(selectedProvider.Name, null, null, null, balance.Remain);
            }
        } catch(Exception e) {
            this._adaptiveRefreshIntervalMinutes = 1;
            this._usageProviderName = null;
            ErrorMessage = e.Message;
            CreditText = "-";
            ClearUsageDisplay();
            CurrentMode = UiMode.BalanceWithoutMax;
        } finally {
            IsRefreshing = false;
            if (this._pendingProviderName is string pendingProviderName) {
                this._pendingProviderName = null;
                await SelectProviderAsync(pendingProviderName);
            }
            if (this._refreshRequested) await RefreshCreditAsync();
            ScheduleNextRefresh();
        }
    }

    async Task ChangeProviderAsync(int direction) {
        if (this._providerList.Count <= 1) return;

        int currentIndex = this._providerList.FindIndex(provider => provider.Name == this._pendingProviderName);
        if (currentIndex < 0) currentIndex = this._selectedProviderIndex;
        int nextIndex = (currentIndex + direction + this._providerList.Count) % this._providerList.Count;
        await SelectProviderAsync(this._providerList[nextIndex].Name);
    }

    /// <summary>
    /// 지정한 프로바이더로 전환하며 조회 중에는 마지막 선택을 보관합니다.
    /// </summary>
    async Task SelectProviderAsync(string providerName) {
        int index = this._providerList.FindIndex(provider => provider.Name == providerName);
        if (index < 0) return;
        if (IsRefreshing) {
            this._pendingProviderName = providerName;
            return;
        }
        if (index == this._selectedProviderIndex) return;

        this._selectedProviderIndex = index;
        UpdateProviderSelection();
        SaveSelectedProvider();
        await RefreshCreditAsync();
    }

    /// <summary>
    /// 목록에서 현재 선택한 프로바이더를 표시합니다.
    /// </summary>
    void UpdateProviderSelection() {
        for (int index = 0; index < AvailableProviders.Count; index++) {
            AvailableProviders[index].IsSelected = index == this._selectedProviderIndex;
        }
    }

    void SaveSelectedProvider() {
        if (this._providerList.Count == 0) return;

        App.Settings.SelectedProviderName = this._providerList[this._selectedProviderIndex].Name;
        AppSettingsStore.Save(App.Settings);
    }

    void PrepareProviderList() {
        this._pendingProviderName = null;
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

        AvailableProviders.Clear();
        foreach (var provider in this._providerList) {
            AvailableProviders.Add(new ProviderListItem(provider.Name));
        }
        UpdateProviderSelection();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasMultipleProviders)));

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

    /// <summary>
    /// 최초 조회 또는 프로바이더 전환 시 사용량 게이지를 0%로 초기화합니다.
    /// </summary>
    void InitializeUsageDisplay(ILlmProvider provider) {
        ClearUsageDisplay();
        CreditText = "";
        HourlyQuotaText = "0%";
        WeeklyQuotaText = "0%";
        ShortQuotaLabel = provider.HasShortQuota ? provider.ShortQuotaLabel : provider.LongQuotaLabel;
        LongQuotaLabel = provider.LongQuotaLabel;
        CurrentMode = provider.HasShortQuota && provider.HasLongQuota
            ? UiMode.DualQuota
            : provider.HasShortQuota || provider.HasLongQuota ? UiMode.SingleQouta : UiMode.Loading;
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
        ThirdQuotaValue = null;
        ThirdQuotaMax = null;
        ThirdQuotaLabel = "";
        ThirdQuotaFormat = "{0:0.#}%";
        this._shortResetAt = null;
        this._longResetAt = null;
        UpdateRemainingTimes();
    }
}
