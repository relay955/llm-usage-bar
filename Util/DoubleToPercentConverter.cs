using System.Globalization;
using System.Windows.Data;

namespace LLMUsageBar.Util;

/// <summary>
/// 0~1 범위의 double 값을 0~100 범위의 double로 변환합니다.
/// 프로그레스바의 Value 바인딩에 사용합니다.
/// </summary>
public class DoubleToPercentConverter : IValueConverter {
    /// <summary>
    /// 0~1 범위의 값을 0~100 범위의 값으로 변환합니다.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is double d) {
            return Math.Clamp(d, 0.0, 1.0) * 100.0;
        }
        return 0.0;
    }

    /// <summary>
    /// 0~100 범위의 값을 0~1 범위로 역변환합니다.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is double d) {
            return Math.Clamp(d, 0.0, 100.0) / 100.0;
        }
        return 0.0;
    }
}
