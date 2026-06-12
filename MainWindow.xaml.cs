using System.Windows;

namespace LLMUsageBar;

public partial class MainWindow : Window {
    private const double TargetWidth = 200;
    private const double EdgePadding = 300;
    private const double DefaultTaskbarHeight = 40;

    public MainWindow() {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
        Width = TargetWidth;
        Topmost = true;

        PlaceNearTaskbarTray();
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
}
