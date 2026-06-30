using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LLMUsageBar.Util;

/// <summary>
/// double 값이 0이 아니면 Visible, 0이면 Collapsed로 변환합니다.
/// Invert 파라미터를 전달하면 결과를 반대로 변환합니다.
/// </summary>
public class NonZeroToVisibilityConverter : IValueConverter {
    /// <summary>
    /// 값이 0이 아닌 경우 Visible, 0인 경우 Collapsed로 변환하고, Invert 파라미터가 있으면 결과를 반대로 바꿉니다.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        bool isVisible = value is double d && d > 0;
        if (parameter is string stringParameter && stringParameter.Equals("Invert", StringComparison.OrdinalIgnoreCase)) {
            isVisible = !isVisible;
        }

        if (isVisible) {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    /// <summary>
    /// Visible은 true로, Collapsed는 false로 역변환합니다.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is Visibility vis && vis == Visibility.Visible) {
            return true;
        }
        return false;
    }
}
