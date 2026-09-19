using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Controls;

/// <summary>
/// Visualisation 512 canaux via WriteableBitmap (zéro allocation par frame).
/// </summary>
public class DmxChannelMonitor : UserControl
{
    private const int ChannelCount = 512;
    private const int Columns = 32;
    private const int Rows = 16;
    private const int CellWidth = 14;
    private const int CellHeight = 22;
    private const int Gap = 2;

    public static readonly DependencyProperty DmxEngineProperty =
        DependencyProperty.Register(nameof(DmxEngine), typeof(IDmxEngine), typeof(DmxChannelMonitor),
            new PropertyMetadata(null, OnDmxEngineChanged));

    private readonly Image _image;
    private readonly WriteableBitmap _bitmap;
    private readonly byte[] _levels = new byte[ChannelCount];
    private readonly byte[] _pixelBuffer;
    private readonly int _stride;
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;
    private System.Windows.Threading.DispatcherTimer? _timer;

    public IDmxEngine? DmxEngine
    {
        get => (IDmxEngine?)GetValue(DmxEngineProperty);
        set => SetValue(DmxEngineProperty, value);
    }

    public DmxChannelMonitor()
    {
        _pixelWidth = Columns * (CellWidth + Gap) + Gap;
        _pixelHeight = Rows * (CellHeight + Gap) + Gap;
        _stride = _pixelWidth * 4;
        _pixelBuffer = new byte[_stride * _pixelHeight];
        _bitmap = new WriteableBitmap(_pixelWidth, _pixelHeight, 96, 96, PixelFormats.Bgra32, null);
        _image = new Image
        {
            Source = _bitmap,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Top,
            SnapsToDevicePixels = true
        };
        Content = _image;
        Loaded += (_, _) => RestartTimer();
        Unloaded += (_, _) => _timer?.Stop();
    }

    private static void OnDmxEngineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DmxChannelMonitor monitor)
            monitor.RestartTimer();
    }

    private void RestartTimer()
    {
        _timer?.Stop();
        if (DmxEngine is null)
            return;

        _timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _timer.Tick += (_, _) =>
        {
            try { Refresh(); }
            catch { /* ignore render glitches */ }
        };
        _timer.Start();
        Refresh();
    }

    private void Refresh()
    {
        if (DmxEngine is null)
            return;

        DmxEngine.CopyActiveUniverseSnapshot(_levels);
        RenderBitmap();
    }

    private void RenderBitmap()
    {
        var pixels = _pixelBuffer;
        var bg = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF); // transparent : le verre reste visible
        var cell = ThemeColor("MonitorCellColor", Color.FromArgb(0x2E, 0x1B, 0x2B, 0x4B));
        var low = ThemeColor("MonitorBarLowColor", Color.FromRgb(0x0A, 0x84, 0xFF));
        var high = ThemeColor("MonitorBarHighColor", Color.FromRgb(0x3A, 0xC4, 0xFF));
        Fill(pixels, bg);

        for (var i = 0; i < ChannelCount; i++)
        {
            var col = i % Columns;
            var row = i / Columns;
            var x0 = Gap + col * (CellWidth + Gap);
            var y0 = Gap + row * (CellHeight + Gap);
            var level = _levels[i] / 255.0;
            var barH = (int)Math.Round(level * CellHeight);
            var color = Lerp(low, high, level);

            for (var y = y0; y < y0 + CellHeight; y++)
                for (var x = x0; x < x0 + CellWidth; x++)
                    SetPixel(pixels, x, y, cell);

            for (var y = y0 + CellHeight - barH; y < y0 + CellHeight; y++)
            {
                for (var x = x0; x < x0 + CellWidth; x++)
                    SetPixel(pixels, x, y, color);
            }
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, _pixelWidth, _pixelHeight), pixels, _stride, 0);
    }

    private static Color ThemeColor(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) is Color c ? c : fallback;

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static void Fill(byte[] pixels, Color color)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }
    }

    private void SetPixel(byte[] pixels, int x, int y, Color color)
    {
        if (x < 0 || y < 0 || x >= _pixelWidth || y >= _pixelHeight)
            return;

        var i = y * _stride + x * 4;
        pixels[i] = color.B;
        pixels[i + 1] = color.G;
        pixels[i + 2] = color.R;
        pixels[i + 3] = color.A;
    }
}
