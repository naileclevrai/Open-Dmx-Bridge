using System.Globalization;
using System.Windows.Data;

namespace OpenDMXBridge.Converters;

public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            Services.Contracts.LogLevel.Trace => "Trace",
            Services.Contracts.LogLevel.Debug => "Debug",
            Services.Contracts.LogLevel.Info => "Info",
            Services.Contracts.LogLevel.Warning => "Warning",
            Services.Contracts.LogLevel.Error => "Error",
            _ => value?.ToString() ?? ""
        };

        var color = key switch
        {
            "Error" => "#FF3B30",
            "Warning" or "WARN" => "#B25E00",
            "Info" => "#1D1D1F",
            "Debug" => "#7A7A7A",
            "Trace" => "#9A9A9A",
            _ => "#1B1B1B"
        };

        return new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color)!);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
