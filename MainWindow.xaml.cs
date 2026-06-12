using System.Windows;

namespace LLMUsageBar;

public partial class MainWindow : Window {
    const double TargetWidth = 230;
    const double EdgePadding = 300;
    const double DefaultTaskbarHeight = 40;

    readonly MainWindowVm _vm;

    public MainWindow() {
        InitializeComponent();
        _vm = (FindResource("Vm") as MainWindowVm)!;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
        Width = TargetWidth;
        Topmost = true;

        PlaceNearTaskbarTray();
        _vm.Start();
    }

    /// <summary>
    /// 작업 표시줄 트레이 근처에 창을 위치시킵니다.
    /// 화면의 작업 영역(`SystemParameters.WorkArea`)과 화면의 너비 및 높이를 기준으로
    /// 작업 표시줄의 위치를 분석하고, 창의 크기와 위치를 적절히 조정합니다.
    /// </summary>
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

    static double GetTaskbarLikeHeight(double taskbarHeight) => Math.Clamp(taskbarHeight, 32, 80);

    void OnClosed(object? sender, EventArgs e) => this._vm.Stop();
}
