using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using LLMUsageBar.Provider;
using LLMUsageBar.ui;

namespace LLMUsageBar;

public partial class MainWindow : Window,INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    
    private const string OpenRouterApiKey = "";
    private const double TargetWidth = 230;
    private const double EdgePadding = 300;
    private const double DefaultTaskbarHeight = 40;

    private readonly DispatcherTimer _refreshTimer = new();
    private bool _isRefreshing;

    public string CreditText { get; set; } = "OpenRouter 조회 중...";

    public MainWindow() {
        InitializeComponent();
        DataContext = this;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
        Width = TargetWidth;
        Topmost = true;

        PlaceNearTaskbarTray();
        ConfigureRefreshTimer();
        _ = RefreshCreditAsync();
    }

    private void PlaceNearTaskbarTray() {
        Rect workArea = SystemParameters.WorkArea;
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;

        if (workArea.Bottom < screenHeight) {
            double taskbarHeight = screenHeight - workArea.Bottom;
            Height = GetTaskbarLikeHeight(taskbarHeight);
            Left = screenWidth - Width - EdgePadding;
            Top = workArea.Bottom + ((taskbarHeight - Height) / 2);
            return;
        }

        if (workArea.Top > 0) {
            double taskbarHeight = workArea.Top;
            Height = GetTaskbarLikeHeight(taskbarHeight);
            Left = screenWidth - Width - EdgePadding;
            Top = (taskbarHeight - Height) / 2;
            return;
        }

        if (workArea.Right < screenWidth) {
            Height = DefaultTaskbarHeight;
            Left = workArea.Right;
            Top = screenHeight - Height - EdgePadding;
            return;
        }

        if (workArea.Left > 0) {
            Height = DefaultTaskbarHeight;
            Left = 0;
            Top = screenHeight - Height - EdgePadding;
            return;
        }

        Height = DefaultTaskbarHeight;
        Left = screenWidth - Width - EdgePadding;
        Top = screenHeight - Height - EdgePadding;
    }

    private static double GetTaskbarLikeHeight(double taskbarHeight) {
        return Math.Clamp(taskbarHeight, 32, 80);
    }

    private async void OpenSettingsButton_Click(object sender, RoutedEventArgs e) {
        var settingsWindow = new Settings {
            Owner = this
        };

        if (settingsWindow.ShowDialog() == true) {
            ConfigureRefreshTimer();
            await RefreshCreditAsync();
        }
    }

    private void ConfigureRefreshTimer() {
        this._refreshTimer.Stop();
        this._refreshTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, App.Settings.RefreshIntervalMinutes));
        this._refreshTimer.Tick -= RefreshTimer_Tick;
        this._refreshTimer.Tick += RefreshTimer_Tick;
        this._refreshTimer.Start();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e) {
        await RefreshCreditAsync();
    }

    private async Task RefreshCreditAsync() {
        if (this._isRefreshing) {
            return;
        }

        if (string.IsNullOrWhiteSpace(OpenRouterApiKey)) {
            CreditText = "OpenRouter API 키 필요";
            return;
        }

        this._isRefreshing = true;

        try {
            var provider = new OpenRouterProvider(OpenRouterApiKey);
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

    private void OnClosed(object? sender, EventArgs e) {
        this._refreshTimer.Stop();
    }
}
