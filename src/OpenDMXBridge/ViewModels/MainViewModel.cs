using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly INetworkService _network;
    private readonly IDmxEngine _dmxEngine;
    private readonly IDmxOutputFactory _outputFactory;
    private readonly IBridgeOrchestrator _bridge;
    private readonly ILoggingService _logger;
    private readonly IFtdiDriverStatus _ftdiDriverStatus;
    private readonly DispatcherTimer _uiTimer;

    public MainViewModel(
        ISettingsService settings,
        INetworkService network,
        IDmxEngine dmxEngine,
        IDmxOutputFactory outputFactory,
        IBridgeOrchestrator bridge,
        ILoggingService logger,
        IFtdiDriverStatus ftdiDriverStatus)
    {
        _settings = settings;
        _network = network;
        _dmxEngine = dmxEngine;
        _outputFactory = outputFactory;
        _bridge = bridge;
        _logger = logger;
        _ftdiDriverStatus = ftdiDriverStatus;

        LogEntries = new ObservableCollection<LogEntry>();
        NetworkAdapters = new ObservableCollection<NetworkAdapterInfo>();
        OutputTypes = new ObservableCollection<string>(_outputFactory.AvailableOutputTypes);
        OutputDevices = new ObservableCollection<DmxOutputDevice>();

        _logger.EntryAdded += OnLogEntryAdded;

        _uiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(66)
        };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();

        RefreshAdapters();
        RefreshOutputDevices();
        LoadFromSettings();
        UpdateFtdiDriverStatus();
    }

    public ObservableCollection<LogEntry> LogEntries { get; }
    public ObservableCollection<NetworkAdapterInfo> NetworkAdapters { get; }
    public ObservableCollection<string> OutputTypes { get; }
    public ObservableCollection<DmxOutputDevice> OutputDevices { get; }

    [ObservableProperty] private bool _isBridgeRunning;
    [ObservableProperty] private bool _artNetActive;
    [ObservableProperty] private bool _dmxOutputConnected;
    [ObservableProperty] private double _dmxFps;
    [ObservableProperty] private double _artNetFps;
    [ObservableProperty] private long _packetsReceived;
    [ObservableProperty] private long _packetsSent;
    [ObservableProperty] private long _invalidPackets;
    [ObservableProperty] private long _lostSequences;
    [ObservableProperty] private long _outOfOrderPackets;
    [ObservableProperty] private double _lastPacketMs;
    [ObservableProperty] private string _outputInfo = "Non connecté";
    [ObservableProperty] private string _statusText = "Arrêté";
    [ObservableProperty] private string _monitorSource = "—";
    [ObservableProperty] private string _monitorSummary = "—";
    [ObservableProperty] private NetworkAdapterInfo? _selectedAdapter;
    [ObservableProperty] private DmxOutputDevice? _selectedOutputDevice;
    [ObservableProperty] private string _selectedOutputType = "OpenDMX";
    [ObservableProperty] private BridgeOperationMode _operationMode = BridgeOperationMode.Bridge;
    [ObservableProperty] private int _artNetNet;
    [ObservableProperty] private int _artNetSubNet;
    [ObservableProperty] private int _artNetUniverse;
    [ObservableProperty] private string _universeDisplay = "0.0.0";
    [ObservableProperty] private LogLevel _minimumLogLevel = LogLevel.Info;
    [ObservableProperty] private bool _ftdiDriverAvailable = true;
    [ObservableProperty] private string? _ftdiDriverMessage;
    [ObservableProperty] private bool _showFtdiWarning;

    public bool IsOpenDmxSelected => string.Equals(SelectedOutputType, "OpenDMX", StringComparison.OrdinalIgnoreCase);
    public bool CanUseOpenDmxBridge => !IsOpenDmxSelected || FtdiDriverAvailable;

    partial void OnSelectedOutputTypeChanged(string value)
    {
        RefreshOutputDevices();
        OnPropertyChanged(nameof(IsOpenDmxSelected));
        OnPropertyChanged(nameof(CanUseOpenDmxBridge));
        UpdateFtdiWarningVisibility();
    }

    partial void OnFtdiDriverAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseOpenDmxBridge));
        UpdateFtdiWarningVisibility();
    }
    public bool IsBridgeMode => OperationMode == BridgeOperationMode.Bridge;

    partial void OnOperationModeChanged(BridgeOperationMode value)
    {
        OnPropertyChanged(nameof(IsMonitorMode));
        OnPropertyChanged(nameof(IsBridgeMode));
        UpdateFtdiWarningVisibility();
    }

    partial void OnMinimumLogLevelChanged(LogLevel value) => _logger.MinimumLevel = value;

    partial void OnArtNetNetChanged(int value) => UpdateUniverseDisplay();
    partial void OnArtNetSubNetChanged(int value) => UpdateUniverseDisplay();
    partial void OnArtNetUniverseChanged(int value) => UpdateUniverseDisplay();

    public bool IsMonitorMode => OperationMode == BridgeOperationMode.Monitor;

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

        if (OperationMode == BridgeOperationMode.Bridge && IsOpenDmxSelected && !FtdiDriverAvailable)
        {
            _logger.Warning(
                FtdiDriverMessage ?? "FTD2XX.dll absente — utilisez le mode Monitor ou installez le pilote FTDI.",
                nameof(MainViewModel));
            return;
        }

        PersistSettings();
        _bridge.Mode = OperationMode;

        _network.SetTargetUniverse(new UniverseId(
            (byte)Math.Clamp(ArtNetNet, 0, 127),
            (byte)Math.Clamp(ArtNetSubNet, 0, 15),
            (byte)Math.Clamp(ArtNetUniverse, 0, 15)));

        if (OperationMode == BridgeOperationMode.Bridge && SelectedOutputDevice is not null)
        {
            var output = _outputFactory.Create(SelectedOutputType);
            await output.ConnectAsync(SelectedOutputDevice);
        }

        await _bridge.StartAsync();
        IsBridgeRunning = true;
        StatusText = OperationMode == BridgeOperationMode.Monitor ? "Analyse" : "En cours";
    }

    [RelayCommand]
    private void RefreshAdapters()
    {
        NetworkAdapters.Clear();
        foreach (var adapter in _network.GetNetworkAdapters())
            NetworkAdapters.Add(adapter);

        var savedId = _settings.Current.SelectedNetworkAdapterId;
        SelectedAdapter = null;
        foreach (var adapter in NetworkAdapters)
        {
            if (adapter.Id == savedId)
            {
                SelectedAdapter = adapter;
                break;
            }
        }

        SelectedAdapter ??= NetworkAdapters.Count > 0 ? NetworkAdapters[0] : null;
    }

    [RelayCommand]
    private void RefreshOutputDevices()
    {
        OutputDevices.Clear();
        var output = _outputFactory.Create(SelectedOutputType);
        foreach (var device in output.EnumerateDevices())
            OutputDevices.Add(device);

        var savedId = _settings.Current.OutputDeviceId;
        SelectedOutputDevice = null;
        foreach (var device in OutputDevices)
        {
            if (device.Id == savedId)
            {
                SelectedOutputDevice = device;
                break;
            }
        }

        SelectedOutputDevice ??= OutputDevices.Count > 0 ? OutputDevices[0] : null;
        OutputInfo = FtdiDriverAvailable
            ? SelectedOutputDevice?.Description ?? "Aucun périphérique"
            : FtdiDriverMessage ?? "Sortie OpenDMX indisponible";
    }

    [RelayCommand]
    private void ClearLogs() => LogEntries.Clear();

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Journal (*.log)|*.log|Tous les fichiers|*.*",
            FileName = $"OpenDMXBridge_{DateTime.Now:yyyyMMdd_HHmmss}.log"
        };

        if (dialog.ShowDialog() != true)
            return;

        await _logger.ExportToFileAsync(dialog.FileName);
        _logger.Info($"Journal exporté : {dialog.FileName}", nameof(MainViewModel));
    }

    private void LoadFromSettings()
    {
        var s = _settings.Current;
        ArtNetNet = s.ArtNetNet;
        ArtNetSubNet = s.ArtNetSubNet;
        ArtNetUniverse = s.ArtNetUniverse;
        SelectedOutputType = s.OutputType;
        OperationMode = s.OperationMode;
        MinimumLogLevel = s.MinimumLogLevel;
        _logger.MinimumLevel = s.MinimumLogLevel;
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
            s.OutputType = SelectedOutputType;
            s.OutputDeviceId = SelectedOutputDevice?.Id;
            s.OperationMode = OperationMode;
            s.MinimumLogLevel = MinimumLogLevel;
        });
        _settings.Save();
    }

    private void UpdateUniverseDisplay() =>
        UniverseDisplay = $"{ArtNetNet}.{ArtNetSubNet}.{ArtNetUniverse}";

    private void UpdateFtdiDriverStatus()
    {
        _ftdiDriverStatus.Probe();
        FtdiDriverAvailable = _ftdiDriverStatus.IsAvailable;
        FtdiDriverMessage = _ftdiDriverStatus.UnavailableMessage;
        UpdateFtdiWarningVisibility();
    }

    private void UpdateFtdiWarningVisibility() =>
        ShowFtdiWarning = !FtdiDriverAvailable && (IsOpenDmxSelected || IsBridgeMode);

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        var stats = _bridge.GetStatistics();
        var monitor = _bridge.GetMonitorSnapshot();

        ArtNetActive = stats.ArtNetListening && !stats.ArtNetTimedOut;
        DmxOutputConnected = stats.DmxOutputConnected;
        DmxFps = stats.DmxFps;
        ArtNetFps = stats.ArtNetFps;
        PacketsReceived = stats.PacketsReceived;
        PacketsSent = stats.PacketsSent;
        InvalidPackets = stats.InvalidPackets;
        LostSequences = stats.LostSequences;
        OutOfOrderPackets = stats.OutOfOrderPackets;
        LastPacketMs = stats.LastPacketMs;
        OutputInfo = stats.OutputDescription ?? OutputInfo;
        MonitorSource = monitor.Source;

        MonitorSummary = $"Univers {monitor.Universe}\n" +
                         $"FPS Art-Net : {monitor.ArtNetFps:F1}\n" +
                         $"FPS DMX : {monitor.DmxFps:F1}\n" +
                         $"Source : {monitor.Source}\n" +
                         $"Paquets : {monitor.PacketsReceived:N0}\n" +
                         $"Dernier paquet : {(double.IsPositiveInfinity(monitor.LastPacketMs) ? "—" : $"{monitor.LastPacketMs:F0} ms")}";
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
