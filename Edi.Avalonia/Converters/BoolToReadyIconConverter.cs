using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Edi.Avalonia.Converters;

public class BoolToReadyIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "✅" : "🚫";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}