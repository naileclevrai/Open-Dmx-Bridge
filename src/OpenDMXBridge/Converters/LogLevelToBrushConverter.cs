using System.Globalization;
using System.Windows.Data;

namespace OpenDMXBridge.Converters;

public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = value?.ToString() switch
        {
            "Error" => "#E04B4B",
            "Warning" => "#E8A838",
            "Info" => "#8AB4F8",
            "Debug" => "#6B7280",
            _ => "#C8CCD4"
        };

        return new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color)!);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
