using System.Globalization;
using System.Windows;
using System.Windows.Data;

using LLMUsageBar;

namespace LLMUsageBar.Util;

/// <summary>
/// 현재 UI 모드가 대상 모드인지 확인하여 Visibility로 변환합니다.
/// </summary>
public sealed class UiModeToVisibilityConverter : IValueConverter {
    /// <summary>
    /// 현재 UI 모드와 ConverterParameter로 전달된 대상 모드를 비교합니다.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is not UiMode currentMode ||
            !Enum.TryParse(parameter as string, out UiMode targetMode)) {
            return Visibility.Collapsed;
        }

        return currentMode == targetMode
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// 역변환은 지원하지 않습니다.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        throw new NotImplementedException();
    }
}
