using System;
using System.Globalization;
using System.Windows.Data;

namespace NutLens.App.Views;

/// <summary>列挙値とラジオボタンの対応づけ。ConverterParameter に対応する値を渡す。</summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && value.Equals(parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // チェックが外れた側からも呼ばれる。外れた側で書き戻すと選択が壊れるので無視する。
        if (value is bool isChecked && isChecked)
        {
            return parameter;
        }

        return Binding.DoNothing;
    }
}

/// <summary>真偽を反転する。「撮影中は操作できない」の表現に使う。</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool flag || !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool flag || !flag;
}
