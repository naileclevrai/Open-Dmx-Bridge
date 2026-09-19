using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ChannelMonitor.DmxEngine = App.Services.GetService(typeof(IDmxEngine)) as IDmxEngine;
        Closing += OnClosing;
    }

    // --- Flou système « Liquid Glass » (Windows 11 22H2+) ---
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmSbtTransientWindow = 3; // Acrylic

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryEnableAcrylicBackdrop();
    }

    /// <summary>Active le fond Acrylic du système derrière la fenêtre ; sinon garde le dégradé de repli.</summary>
    private void TryEnableAcrylicBackdrop()
    {
        try
        {
            if (Environment.OSVersion.Version.Build < 22621)
                return;

            var hwnd = new WindowInteropHelper(this).Handle;
            var backdrop = DwmSbtTransientWindow;
            if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) != 0)
                return;

            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
                target.BackgroundColor = Colors.Transparent;

            Background = Brushes.Transparent;
        }
        catch
        {
            // Fond dégradé conservé.
        }
    }

    // --- Sélecteur segmenté : indicateur qui glisse sous l'onglet actif ---
    private static readonly IEasingFunction TabEase = new CubicEase { EasingMode = EasingMode.EaseOut };

    private void OnTabsLoaded(object sender, RoutedEventArgs e) => MoveTabIndicator(animate: false);

    private void OnTabsSizeChanged(object sender, SizeChangedEventArgs e) => MoveTabIndicator(animate: false);

    private void OnTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabs))
            return;

        MoveTabIndicator(animate: true);
        SlideTabContent();
    }

    private int _lastTabIndex = -1;

    /// <summary>Le contenu du nouvel onglet glisse depuis la droite (ou la gauche) en fondu.</summary>
    private void SlideTabContent()
    {
        var index = MainTabs.SelectedIndex;
        var direction = _lastTabIndex < 0 || index >= _lastTabIndex ? 1 : -1;
        _lastTabIndex = index;

        if (MainTabs.Template?.FindName("PART_SelectedContentHost", MainTabs) is not ContentPresenter host)
            return;

        // La transformation issue du template est gelée : on en pose une neuve, animable.
        var slide = new TranslateTransform();
        host.RenderTransform = slide;

        // État de départ posé tout de suite pour éviter qu'une image s'affiche à pleine opacité
        // avant le premier tick de l'animation (clignotement visible).
        var offset = 36.0 * direction;
        slide.X = offset;
        host.Opacity = 0;

        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(offset, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = TabEase });
            host.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = TabEase });
        });
    }

    private void MoveTabIndicator(bool animate)
    {
        if (MainTabs.SelectedIndex < 0 || MainTabs.Template?.FindName("PART_Indicator", MainTabs) is not Border indicator)
            return;

        if (MainTabs.ItemContainerGenerator.ContainerFromIndex(MainTabs.SelectedIndex) is not TabItem item
            || item.ActualWidth <= 0 || indicator.Parent is not UIElement canvas)
        {
            Dispatcher.BeginInvoke(() => MoveTabIndicator(animate), DispatcherPriority.Loaded);
            return;
        }

        var target = item.TranslatePoint(new Point(0, 0), canvas);
        var duration = animate ? TimeSpan.FromMilliseconds(280) : TimeSpan.Zero;
        indicator.Height = item.ActualHeight;
        indicator.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(target.X, duration) { EasingFunction = TabEase });
        indicator.BeginAnimation(WidthProperty, new DoubleAnimation(item.ActualWidth, duration) { EasingFunction = TabEase });
    }

    // --- Faders : glissement robuste ---
    // Le Slider WPF n'engage le glissement que si l'appui a lieu sur le bouton rond ;
    // un appui sur la piste saute à la valeur puis « lâche » la souris. Ici le fader
    // capture la souris dès l'appui, où qu'il soit, et suit le pointeur jusqu'au relâchement.

    private static void SetValueFromPointer(Slider slider, MouseEventArgs e)
    {
        // Calcul ABSOLU depuis la géométrie de la piste. Track.ValueFromPoint est relatif à la
        // dernière position dessinée du bouton : deux événements souris avant un re-layout
        // comptent le déplacement deux fois et le fader saute loin du pointeur.
        var vertical = slider.Orientation == Orientation.Vertical;
        double ratio;
        if (slider.Template?.FindName("PART_Track", slider) is Track track)
        {
            var p = e.GetPosition(track);
            var thumbLen = vertical ? track.Thumb?.ActualHeight ?? 0 : track.Thumb?.ActualWidth ?? 0;
            var length = Math.Max(1.0, (vertical ? track.ActualHeight : track.ActualWidth) - thumbLen);
            var pos = (vertical ? p.Y : p.X) - thumbLen / 2.0;
            ratio = pos / length;
            if (vertical)
                ratio = 1.0 - ratio; // en vertical, le haut de la piste vaut le maximum
        }
        else
        {
            var p = e.GetPosition(slider);
            ratio = vertical
                ? 1.0 - p.Y / Math.Max(1.0, slider.ActualHeight)
                : p.X / Math.Max(1.0, slider.ActualWidth);
        }

        if (double.IsNaN(ratio))
            return;

        ratio = Math.Clamp(ratio, 0.0, 1.0);
        var value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
        slider.Value = Math.Clamp(Math.Round(value), slider.Minimum, slider.Maximum);
    }

    private void OnFaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsEnabled)
            return;

        SetValueFromPointer(slider, e);
        slider.CaptureMouse();
        slider.Focus();
        e.Handled = true;
    }

    private void OnFaderMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsMouseCaptured)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            slider.ReleaseMouseCapture();
            return;
        }

        SetValueFromPointer(slider, e);
        e.Handled = true;
    }

    private void OnFaderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsMouseCaptured)
            return;

        SetValueFromPointer(slider, e);
        slider.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnFaderLostCapture(object sender, MouseEventArgs e)
    {
        // Rien à faire : la valeur courante reste tenue par la console.
    }
    private void OnFlashDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ViewModels.ConsoleFaderViewModel fader })
            fader.FlashOn();
    }

    private void OnFlashUp(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ViewModels.ConsoleFaderViewModel fader })
            fader.FlashOff();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.SaveSettings();
    }
}
