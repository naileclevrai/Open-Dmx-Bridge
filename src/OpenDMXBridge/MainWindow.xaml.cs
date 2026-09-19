using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
