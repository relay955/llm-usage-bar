using System.Globalization;
using System.Windows.Data;

namespace LLMUsageBar.Util;

/// <summary>
/// MaxBalance와 BalanceRatio를 받아 "$X.XX(YY%)" 형태의 텍스트를 생성합니다.
/// MaxBalance가 0이면 null을 반환하여 Visibility: Hidden 처리합니다.
/// </summary>
public class BalanceTextConverter : IMultiValueConverter {
    /// <summary>
    /// MaxBalance와 BalanceRatio 바인딩 값을 받아 "$X.XX(YY%)" 문자열로 변환합니다.
    /// </summary>
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) {
        if (values == null || values.Length < 2)
            return Binding.DoNothing;

        if (values[0] is not double maxBalance || maxBalance <= 0)
            return Binding.DoNothing;

        if (values[1] is not double ratio)
            return Binding.DoNothing;

        double remain = maxBalance * ratio;
        int percent = (int)Math.Round(ratio * 100);
        return $"${remain:0.00}({percent}%)";
    }

    /// <summary>
    /// 역변환은 지원하지 않습니다.
    /// </summary>
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) {
        throw new NotImplementedException();
    }
}
