using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LLMUsageBar.ui;

public partial class ProviderPickerControl : UserControl {
    public static readonly DependencyProperty PlacementTargetProperty = DependencyProperty.Register(
        nameof(PlacementTarget), typeof(FrameworkElement), typeof(ProviderPickerControl),
        new PropertyMetadata(null, OnPlacementTargetChanged));
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ProviderPickerControl));
    public static readonly DependencyProperty SelectionCommandProperty = DependencyProperty.Register(
        nameof(SelectionCommand), typeof(ICommand), typeof(ProviderPickerControl));

    readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public FrameworkElement? PlacementTarget {
        get => (FrameworkElement?)GetValue(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    public IEnumerable? ItemsSource {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand? SelectionCommand {
        get => (ICommand?)GetValue(SelectionCommandProperty);
        set => SetValue(SelectionCommandProperty, value);
    }

    public bool IsOpen => ProviderPopup.IsOpen;

    public ProviderPickerControl() {
        InitializeComponent();
        _closeTimer.Tick += CloseTimer_Tick;
        Loaded += (_, _) => AttachTarget(PlacementTarget);
        Unloaded += (_, _) => {
            Close();
            DetachTarget(PlacementTarget);
        };
    }

    /// <summary>
    /// 지연 닫기 타이머를 중지하고 프로바이더 목록을 닫습니다.
    /// </summary>
    public void Close() {
        _closeTimer.Stop();
        ProviderPopup.IsOpen = false;
    }

    static void OnPlacementTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) {
        var control = (ProviderPickerControl)sender;
        control.Close();
        if (!control.IsLoaded) return;
        control.DetachTarget(e.OldValue as FrameworkElement);
        control.AttachTarget(e.NewValue as FrameworkElement);
    }

    void AttachTarget(FrameworkElement? target) {
        if (target is null) return;
        target.MouseEnter += Target_MouseEnter;
        target.MouseLeave += ProviderArea_MouseLeave;
        target.PreviewMouseLeftButtonDown += Target_PreviewMouseLeftButtonDown;
    }

    void DetachTarget(FrameworkElement? target) {
        if (target is null) return;
        target.MouseEnter -= Target_MouseEnter;
        target.MouseLeave -= ProviderArea_MouseLeave;
        target.PreviewMouseLeftButtonDown -= Target_PreviewMouseLeftButtonDown;
    }

    void Target_MouseEnter(object sender, MouseEventArgs e) {
        _closeTimer.Stop();
        if (!IsLoaded || PlacementTarget?.IsVisible != true || !ProviderItems.HasItems) return;
        ProviderPopup.IsOpen = true;
    }

    void Popup_MouseEnter(object sender, MouseEventArgs e) => _closeTimer.Stop();

    void ProviderArea_MouseLeave(object sender, MouseEventArgs e) {
        _closeTimer.Stop();
        _closeTimer.Start();
    }

    void CloseTimer_Tick(object? sender, EventArgs e) {
        _closeTimer.Stop();
        if (PlacementTarget?.IsMouseOver != true && !PopupBorder.IsMouseOver) Close();
    }

    void Target_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (!PopupBorder.IsMouseOver) Close();
    }

    void Popup_Click(object sender, RoutedEventArgs e) {
        if (e.OriginalSource is Button { CommandParameter: ProviderListItem }) Close();
    }

    void Popup_PreviewKeyDown(object sender, KeyEventArgs e) {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }
}
