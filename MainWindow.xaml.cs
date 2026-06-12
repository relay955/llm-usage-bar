using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace LLMUsageBar;

public partial class MainWindow : Window {
    static readonly IntPtr HwndTopmost = new(-1);
    const uint SwpNoSize = 0x0001;
    const uint SwpNoMove = 0x0002;
    const uint SwpNoActivate = 0x0010;
    const uint SwpShowWindow = 0x0040;

    const double TargetWidth = 230;
    const double EdgePadding = 300;
    const double DefaultTaskbarHeight = 40;

    readonly MainWindowVm _vm;
    readonly DispatcherTimer _topmostTimer = new();

    public MainWindow() {
        InitializeComponent();
        _vm = (FindResource("Vm") as MainWindowVm)!;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    void OnLoaded(object sender, RoutedEventArgs e) {
        Width = TargetWidth;

        PlaceNearTaskbarTray();
        KeepAboveTaskbar();
        _vm.StartTimer();
    }

    /// <summary>
    /// 작업 표시줄 트레이 근처에 창을 위치시킵니다.
    /// 화면의 작업 영역(`SystemParameters.WorkArea`)과 화면의 너비 및 높이를 기준으로
    /// 작업 표시줄의 위치를 분석하고, 창의 크기와 위치를 적절히 조정합니다.
    /// </summary>
    void PlaceNearTaskbarTray() {
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

    static double GetTaskbarLikeHeight(double taskbarHeight) => Math.Clamp(taskbarHeight, 32, 80);

    void KeepAboveTaskbar() {
        _topmostTimer.Interval = TimeSpan.FromSeconds(1);
        _topmostTimer.Tick += TopmostTimer_Tick;
        _topmostTimer.Start();
    }

    void TopmostTimer_Tick(object? sender, EventArgs e) {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Topmost = false;
        Topmost = true;
    }

    void OnClosed(object? sender, EventArgs e) => this._vm.StopTimer();
}
