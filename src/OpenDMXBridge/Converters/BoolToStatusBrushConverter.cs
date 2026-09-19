using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OpenDMXBridge.Converters;

public sealed class BoolToStatusBrushConverter : IValueConverter
{
    public Brush ActiveBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59));
    public Brush InactiveBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
