using System.Windows;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ChannelMonitor.DmxEngine = App.Services.GetService(typeof(IDmxEngine)) as IDmxEngine;
    }
}
