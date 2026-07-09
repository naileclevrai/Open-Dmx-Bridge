using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Controls;

/// <summary>
/// Visualisation haute performance des 512 canaux DMX (redraw direct, sans 512 bindings).
/// </summary>
public class DmxChannelMonitor : Canvas
{
    private const int ChannelCount = 512;
    private const int Columns = 32;
    private const int Rows = 16;

    public static readonly DependencyProperty DmxEngineProperty =
        DependencyProperty.Register(nameof(DmxEngine), typeof(IDmxEngine), typeof(DmxChannelMonitor),
            new PropertyMetadata(null, OnDmxEngineChanged));

    private readonly Rectangle[] _bars = new Rectangle[ChannelCount];
    private readonly byte[] _levels = new byte[ChannelCount];
    private System.Windows.Threading.DispatcherTimer? _timer;

    public IDmxEngine? DmxEngine
    {
        get => (IDmxEngine?)GetValue(DmxEngineProperty);
        set => SetValue(DmxEngineProperty, value);
    }

    public DmxChannelMonitor()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x18));
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        InitializeBars();
    }

    private static void OnDmxEngineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DmxChannelMonitor monitor)
            monitor.RestartTimer();
    }

    private void InitializeBars()
    {
        for (var i = 0; i < ChannelCount; i++)
        {
            var rect = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromRgb(0x2A, 0x6F, 0xD8)),
                RadiusX = 1,
                RadiusY = 1
            };
            _bars[i] = rect;
            Children.Add(rect);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => RestartTimer();
    private void OnUnloaded(object sender, RoutedEventArgs e) => _timer?.Stop();

    private void RestartTimer()
    {
        _timer?.Stop();
        if (DmxEngine is null)
            return;

        _timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(66)
        };
        _timer.Tick += (_, _) => RefreshLevels();
        _timer.Start();
    }

    private void RefreshLevels()
    {
        if (DmxEngine is null)
            return;

        var snapshot = DmxEngine.GetChannelSnapshot();
        snapshot.CopyTo(_levels);
        LayoutBars();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => LayoutBars();

    private void LayoutBars()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var gap = 2.0;
        var cellW = (ActualWidth - gap * (Columns + 1)) / Columns;
        var cellH = (ActualHeight - gap * (Rows + 1)) / Rows;

        for (var i = 0; i < ChannelCount; i++)
        {
            var col = i % Columns;
            var row = i / Columns;
            var x = gap + col * (cellW + gap);
            var y = gap + row * (cellH + gap);
            var level = _levels[i] / 255.0;
            var barH = Math.Max(1, level * (cellH - 2));

            var rect = _bars[i];
            Canvas.SetLeft(rect, x + 1);
            Canvas.SetTop(rect, y + cellH - barH);
            rect.Width = Math.Max(1, cellW - 2);
            rect.Height = barH;

            var intensity = (byte)(80 + level * 175);
            rect.Fill = new SolidColorBrush(Color.FromRgb(0x1A, intensity, (byte)(200 + level * 55)));
        }
    }
}
