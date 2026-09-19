using System.Windows;
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
