using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly INetworkService _network;
    private readonly IDmxEngine _dmxEngine;
    private readonly IFtdiOutputService _ftdi;
    private readonly IBridgeOrchestrator _bridge;
    private readonly ILoggingService _logger;
    private readonly DispatcherTimer _uiTimer;
    private readonly byte[] _channelSnapshot = new byte[512];

    public MainViewModel(
        ISettingsService settings,
        INetworkService network,
        IDmxEngine dmxEngine,
        IFtdiOutputService ftdi,
        IBridgeOrchestrator bridge,
        ILoggingService logger)
    {
        _settings = settings;
        _network = network;
        _dmxEngine = dmxEngine;
        _ftdi = ftdi;
        _bridge = bridge;
        _logger = logger;

        LogEntries = new ObservableCollection<LogEntry>();
        NetworkAdapters = new ObservableCollection<NetworkAdapterInfo>();
        FtdiDevices = new ObservableCollection<FtdiDeviceInfo>();
        ChannelLevels = new ObservableCollection<byte>(Enumerable.Repeat((byte)0, 512));

        _logger.EntryAdded += OnLogEntryAdded;

        _uiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(66)
        };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();

        RefreshAdapters();
        RefreshFtdiDevices();
        LoadFromSettings();
    }

    public ObservableCollection<LogEntry> LogEntries { get; }
    public ObservableCollection<NetworkAdapterInfo> NetworkAdapters { get; }
    public ObservableCollection<FtdiDeviceInfo> FtdiDevices { get; }
    public ObservableCollection<byte> ChannelLevels { get; }

    [ObservableProperty] private bool _isBridgeRunning;
    [ObservableProperty] private bool _artNetActive;
    [ObservableProperty] private bool _ftdiConnected;
    [ObservableProperty] private double _dmxFps;
    [ObservableProperty] private long _packetsReceived;
    [ObservableProperty] private long _packetsSent;
    [ObservableProperty] private long _invalidPackets;
    [ObservableProperty] private string _ftdiInfo = "Non connecté";
    [ObservableProperty] private string _statusText = "Arrêté";
    [ObservableProperty] private NetworkAdapterInfo? _selectedAdapter;
    [ObservableProperty] private FtdiDeviceInfo? _selectedFtdiDevice;
    [ObservableProperty] private int _artNetNet;
    [ObservableProperty] private int _artNetSubNet;
    [ObservableProperty] private int _artNetUniverse;
    [ObservableProperty] private string _universeDisplay = "0.0.0";

    partial void OnArtNetNetChanged(int value) => UpdateUniverseDisplay();
    partial void OnArtNetSubNetChanged(int value) => UpdateUniverseDisplay();
    partial void OnArtNetUniverseChanged(int value) => UpdateUniverseDisplay();

    [RelayCommand]
    private async Task ToggleBridgeAsync()
    {
        if (IsBridgeRunning)
        {
            await _bridge.StopAsync();
            IsBridgeRunning = false;
            StatusText = "Arrêté";
            return;
        }

        PersistSettings();
        _network.SetTargetUniverse(new ArtNetUniverse(
            (byte)Math.Clamp(ArtNetNet, 0, 127),
            (byte)Math.Clamp(ArtNetSubNet, 0, 15),
            (byte)Math.Clamp(ArtNetUniverse, 0, 15)));

        if (SelectedFtdiDevice is not null)
            await _ftdi.ConnectAsync(SelectedFtdiDevice.Index);

        await _bridge.StartAsync();
        IsBridgeRunning = true;
        StatusText = "En cours";
    }

    [RelayCommand]
    private void RefreshAdapters()
    {
        NetworkAdapters.Clear();
        foreach (var adapter in _network.GetNetworkAdapters())
            NetworkAdapters.Add(adapter);

        var savedId = _settings.Current.SelectedNetworkAdapterId;
        SelectedAdapter = NetworkAdapters.FirstOrDefault(a => a.Id == savedId)
                          ?? NetworkAdapters.FirstOrDefault();
    }

    [RelayCommand]
    private void RefreshFtdiDevices()
    {
        FtdiDevices.Clear();
        foreach (var device in _ftdi.EnumerateDevices())
            FtdiDevices.Add(device);

        SelectedFtdiDevice = FtdiDevices.FirstOrDefault();
        FtdiInfo = SelectedFtdiDevice is null
            ? "Aucune interface FTDI"
            : $"{SelectedFtdiDevice.Description} ({SelectedFtdiDevice.SerialNumber})";
    }

    [RelayCommand]
    private void ClearLogs() => LogEntries.Clear();

    private void LoadFromSettings()
    {
        var s = _settings.Current;
        ArtNetNet = s.ArtNetNet;
        ArtNetSubNet = s.ArtNetSubNet;
        ArtNetUniverse = s.ArtNetUniverse;
        UpdateUniverseDisplay();

        foreach (var entry in _logger.GetRecentEntries(200))
            LogEntries.Add(entry);
    }

    private void PersistSettings()
    {
        _settings.Update(s =>
        {
            s.SelectedNetworkAdapterId = SelectedAdapter?.Id;
            s.ArtNetNet = (byte)Math.Clamp(ArtNetNet, 0, 127);
            s.ArtNetSubNet = (byte)Math.Clamp(ArtNetSubNet, 0, 15);
            s.ArtNetUniverse = (byte)Math.Clamp(ArtNetUniverse, 0, 15);
        });
        _settings.Save();
    }

    private void UpdateUniverseDisplay() =>
        UniverseDisplay = $"{ArtNetNet}.{ArtNetSubNet}.{ArtNetUniverse}";

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        var stats = _bridge.GetStatistics();
        ArtNetActive = stats.ArtNetListening && stats.LastArtNetPacket > DateTimeOffset.Now.AddSeconds(-2);
        FtdiConnected = stats.FtdiConnected;
        DmxFps = stats.DmxFps;
        PacketsReceived = stats.PacketsReceived;
        PacketsSent = stats.PacketsSent;
        InvalidPackets = stats.InvalidPackets;
        FtdiInfo = stats.FtdiDescription ?? FtdiInfo;

        var snapshot = _dmxEngine.GetChannelSnapshot();
        snapshot.CopyTo(_channelSnapshot);
        for (var i = 0; i < 512; i++)
            ChannelLevels[i] = _channelSnapshot[i];
    }

    private void OnLogEntryAdded(object? sender, LogEntry entry)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            LogEntries.Add(entry);
            while (LogEntries.Count > 500)
                LogEntries.RemoveAt(0);
        });
    }

    public void Dispose()
    {
        _uiTimer.Stop();
        _logger.EntryAdded -= OnLogEntryAdded;
    }
}
