using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace OpenDMXBridge.Converters;

/// <summary>Couleur d'une ligne de journal selon son niveau, lue dans le thème courant (Themes/*.xaml).</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            Services.Contracts.LogLevel.Error => "LogErrorColor",
            Services.Contracts.LogLevel.Warning => "LogWarningColor",
            Services.Contracts.LogLevel.Debug => "LogDebugColor",
            Services.Contracts.LogLevel.Trace => "LogTraceColor",
            _ => "LogInfoColor"
        };

        var color = Application.Current?.TryFindResource(key) as Color? ?? Colors.Gray;
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
