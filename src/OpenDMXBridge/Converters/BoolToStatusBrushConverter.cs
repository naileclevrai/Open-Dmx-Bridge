using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OpenDMXBridge.Converters;

public sealed class BoolToStatusBrushConverter : IValueConverter
{
    public Brush ActiveBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x3D, 0xC4, 0x6E));
    public Brush InactiveBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0xE0, 0x4B, 0x4B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
