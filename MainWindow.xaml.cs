using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

using LLMUsageBar.Module;

namespace LLMUsageBar;

public partial class MainWindow : Window {
    const double TargetWidth = 230;
    const double EdgePadding = 300;
    const double DefaultTaskbarHeight = 40;
    const uint MonitorDefaultToNearest = 2;

    readonly MainWindowVm _vm;
    readonly DispatcherTimer _topmostTimer = new();
    bool _isPositionInitialized;

    public MainWindow() {
        InitializeComponent();
        _vm = (FindResource("Vm") as MainWindowVm)!;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    void OnLoaded(object sender, RoutedEventArgs e) {
        Width = TargetWidth;

        RestorePositionOrPlaceNearTaskbarTray();
        _isPositionInitialized = true;
        KeepAboveTaskbar();
        _vm.Init();
    }

    void RestorePositionOrPlaceNearTaskbarTray() {
        if (App.Settings.WindowLeft is double left &&
            App.Settings.WindowTop is double top &&
            IsPositionVisible(left, top)) {
            Left = left;
            Top = top;
            return;
        }

        PlaceNearTaskbarTray();
    }

    static bool IsPositionVisible(double left, double top) {
        return left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
               left + TargetWidth > SystemParameters.VirtualScreenLeft &&
               top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight &&
               top + DefaultTaskbarHeight > SystemParameters.VirtualScreenTop;
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
        if (IsFullscreenWindowOnOwnMonitor()) {
            Hide();
            return;
        }

        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Topmost = false;
        Topmost = true;
    }

    bool IsFullscreenWindowOnOwnMonitor() {
        nint ownWindow = new WindowInteropHelper(this).Handle;
        nint ownMonitor = MonitorFromWindow(ownWindow, MonitorDefaultToNearest);
        if (ownMonitor == 0) return false;
        MonitorInfo monitorInfo = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(ownMonitor, ref monitorInfo)) return false;

        bool fullscreenFound = false;
        EnumWindows((window, _) => {
            if (window == ownWindow || !IsWindowVisible(window) || IsIconic(window)) return true;

            StringBuilder className = new(256);
            GetClassName(window, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW") return true;

            if (DwmGetWindowAttribute(window, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            if (!GetWindowRect(window, out NativeRect windowRect)) return true;

            const int tolerance = 2;
            fullscreenFound = windowRect.Left <= monitorInfo.Monitor.Left + tolerance &&
                              windowRect.Top <= monitorInfo.Monitor.Top + tolerance &&
                              windowRect.Right >= monitorInfo.Monitor.Right - tolerance &&
                              windowRect.Bottom >= monitorInfo.Monitor.Bottom - tolerance;
            return !fullscreenFound;
        }, 0);
        return fullscreenFound;
    }

    const int DwmwaCloaked = 14;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    delegate bool EnumWindowsProc(nint window, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsIconic(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern int GetClassName(nint window, StringBuilder className, int maxCount);

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int valueSize);

    [DllImport("user32.dll")]
    static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    void DragHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
        if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;

        DragMove();
        SaveWindowPosition();
    }

    void SaveWindowPosition() {
        if (!_isPositionInitialized || double.IsNaN(Left) || double.IsNaN(Top)) return;

        App.Settings.WindowLeft = Left;
        App.Settings.WindowTop = Top;
        AppSettingsStore.Save(App.Settings);
    }

    void OnClosed(object? sender, EventArgs e) {
        SaveWindowPosition();
        _vm.StopTimer();
    }
}
