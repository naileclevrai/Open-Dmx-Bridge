using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;
using OpenDMXBridge.Services.Ftdi;
using OpenDMXBridge.Services.Outputs;

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
    private readonly ConcurrentQueue<LogEntry> _pendingLogEntries = new();

    public MainViewModel(
        ISettingsService settings,
        INetworkService network,
        IDmxEngine dmxEngine,
        IDmxOutputFactory outputFactory,
        IBridgeOrchestrator bridge,
        ILoggingService logger,
        IFtdiDriverStatus ftdiDriverStatus,
        ConsoleViewModel console)
    {
        _settings = settings;
        _network = network;
        _dmxEngine = dmxEngine;
        _outputFactory = outputFactory;
        _bridge = bridge;
        _logger = logger;
        _ftdiDriverStatus = ftdiDriverStatus;
        Console = console;

        LogEntries = new ObservableCollection<LogEntry>();
        NetworkAdapters = new ObservableCollection<NetworkAdapterInfo>();
        OutputTypes = new ObservableCollection<string>(_outputFactory.AvailableOutputTypes);
        OutputDevices = new ObservableCollection<DmxOutputDevice>();

        OperationModeOptions =
        [
            new LabeledOption<BridgeOperationMode>(BridgeOperationMode.Bridge, "Bridge — Art-Net vers DMX"),
            new LabeledOption<BridgeOperationMode>(BridgeOperationMode.Monitor, "Monitor — analyse réseau")
        ];
        SelectedOperationMode = OperationModeOptions[0];

        LogLevelOptions =
        [
            new LabeledOption<LogLevel>(LogLevel.Trace, "Trace"),
            new LabeledOption<LogLevel>(LogLevel.Debug, "Debug"),
            new LabeledOption<LogLevel>(LogLevel.Info, "Info"),
            new LabeledOption<LogLevel>(LogLevel.Warning, "Warn"),
            new LabeledOption<LogLevel>(LogLevel.Error, "Error")
        ];
        SelectedLogLevel = LogLevelOptions[3];

        _logger.EntryAdded += OnLogEntryAdded;

        _uiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();

        LoadFromSettings();
        RefreshAdapters();
        if (FtdiUsbDiagnostics.FindFtdiComPorts().Count > 0)
            _ftdiDriverStatus.ResetProbe();
        RefreshOutputDevices();
        UpdateFtdiDriverStatus();
    }

    public ConsoleViewModel Console { get; }
    public ObservableCollection<LogEntry> LogEntries { get; }
    public ObservableCollection<NetworkAdapterInfo> NetworkAdapters { get; }
    public ObservableCollection<string> OutputTypes { get; }
    public ObservableCollection<DmxOutputDevice> OutputDevices { get; }
    public ObservableCollection<LabeledOption<BridgeOperationMode>> OperationModeOptions { get; }
    public ObservableCollection<LabeledOption<LogLevel>> LogLevelOptions { get; }

    [ObservableProperty] private LabeledOption<BridgeOperationMode>? _selectedOperationMode;
    [ObservableProperty] private LabeledOption<LogLevel>? _selectedLogLevel;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleBridgeCommand))]
    private bool _isBridgeRunning;
    [ObservableProperty] private bool _artNetActive;
    [ObservableProperty] private bool _dmxOutputConnected;
    [ObservableProperty] private double _dmxFps;
    [ObservableProperty] private double _artNetFps;
    [ObservableProperty] private long _packetsReceived;
    [ObservableProperty] private long _packetsSent;
    [ObservableProperty] private long _invalidPackets;
    [ObservableProperty] private long _lostSequences;
    [ObservableProperty] private long _outOfOrderPackets;
    [ObservableProperty] private string _lastPacketDisplay = "—";
    [ObservableProperty] private string _outputInfo = "Non connecté";
    [ObservableProperty] private string _statusText = "Arrêté";
    [ObservableProperty] private string _monitorSource = "—";
    [ObservableProperty] private string _monitorSummary = "—";
    [ObservableProperty] private NetworkAdapterInfo? _selectedAdapter;
    [ObservableProperty] private DmxOutputDevice? _selectedOutputDevice;
    [ObservableProperty] private string _selectedOutputType = "OpenDMX";
    [ObservableProperty] private int _artNetNet;
    [ObservableProperty] private int _artNetSubNet;
    [ObservableProperty] private int _artNetUniverse;
    [ObservableProperty] private string _universeDisplay = "0.0.0";
    [ObservableProperty] private bool _ftdiDriverAvailable = true;
    [ObservableProperty] private string? _ftdiDriverMessage;
    [ObservableProperty] private bool _showFtdiWarning;

    public BridgeOperationMode OperationMode => SelectedOperationMode?.Value ?? BridgeOperationMode.Bridge;
    public bool IsOpenDmxSelected => string.Equals(SelectedOutputType, "OpenDMX", StringComparison.OrdinalIgnoreCase);
    public bool IsBridgeMode => OperationMode == BridgeOperationMode.Bridge;
    public bool IsMonitorMode => OperationMode == BridgeOperationMode.Monitor;

    partial void OnSelectedOperationModeChanged(LabeledOption<BridgeOperationMode>? value)
    {
        OnPropertyChanged(nameof(OperationMode));
        OnPropertyChanged(nameof(IsBridgeMode));
        OnPropertyChanged(nameof(IsMonitorMode));
        UpdateFtdiWarningVisibility();
    }

    partial void OnSelectedLogLevelChanged(LabeledOption<LogLevel>? value)
    {
        if (value is not null)
            _logger.MinimumLevel = value.Value;
    }

    partial void OnSelectedOutputTypeChanged(string value)
    {
        RefreshOutputDevices();
        OnPropertyChanged(nameof(IsOpenDmxSelected));
        UpdateFtdiWarningVisibility();
    }

    partial void OnFtdiDriverAvailableChanged(bool value) => UpdateFtdiWarningVisibility();

    partial void OnArtNetNetChanged(int value) => UpdateUniverseDisplay();
    partial void OnArtNetSubNetChanged(int value) => UpdateUniverseDisplay();
    partial void OnArtNetUniverseChanged(int value) => UpdateUniverseDisplay();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleBridgeCommand))]
    private bool _isBridgeStarting;

    [RelayCommand(CanExecute = nameof(CanToggleBridge))]
    private async Task ToggleBridgeAsync()
    {
        if (IsBridgeStarting)
            return;

        if (IsBridgeRunning)
        {
            IsBridgeStarting = true;
            StatusText = "Arrêt…";
            try
            {
                await Task.Run(async () => await _bridge.StopAsync().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _logger.Error($"Échec arrêt : {ex.Message}", nameof(MainViewModel));
            }
            finally
            {
                IsBridgeRunning = false;
                IsBridgeStarting = false;
                StatusText = "Arrêté";
                SaveSettings();
            }
            return;
        }

        if (IsBridgeMode && IsOpenDmxSelected && !IsOpenDmxOutputAvailable())
        {
            _logger.Warning(
                FtdiDriverMessage ?? "Aucune interface Open DMX détectée — passez en mode Monitor ou branchez l'Enttec USB.",
                nameof(MainViewModel));
            return;
        }

        IsBridgeStarting = true;
        try
        {
            PersistSettings();
            FlushPendingLogEntries();
            _bridge.Mode = OperationMode;

            _network.SetTargetUniverse(new UniverseId(
                (byte)Math.Clamp(ArtNetNet, 0, 127),
                (byte)Math.Clamp(ArtNetSubNet, 0, 15),
                (byte)Math.Clamp(ArtNetUniverse, 0, 15)));

            StatusText = "Démarrage…";

            await Task.Run(async () => await _bridge.StartAsync().ConfigureAwait(false));

            IsBridgeRunning = true;
            StatusText = IsMonitorMode ? "Analyse" : "En cours";
        }
        catch (Exception ex)
        {
            _logger.Error($"Échec démarrage : {ex.Message}", nameof(MainViewModel));
            StatusText = "Arrêté";
        }
        finally
        {
            IsBridgeStarting = false;
        }
    }

    private bool CanToggleBridge() => !IsBridgeStarting;

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
        if (IsOpenDmxSelected)
        {
            var comPorts = FtdiUsbDiagnostics.FindFtdiComPorts();
            _ftdiDriverStatus.ResetProbe();
            if (comPorts.Count == 0)
                _ftdiDriverStatus.Probe();

            var openDmx = (OpenDmxOutput)_outputFactory.Create("OpenDMX");
            FtdiDriverAvailable = openDmx.IsDriverAvailable;
            FtdiDriverMessage = openDmx.DriverUnavailableMessage;
            UpdateFtdiWarningVisibility();
        }

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

        // Préférer le port série quand D2XX est verrouillé par le pilote VCP.
        if (SelectedOutputDevice is null && IsOpenDmxSelected)
        {
            SelectedOutputDevice = OutputDevices.FirstOrDefault(d =>
                d.Id.StartsWith("d2xx:", StringComparison.OrdinalIgnoreCase));
        }

        SelectedOutputDevice ??= OutputDevices.Count > 0 ? OutputDevices[0] : null;

        if (!FtdiDriverAvailable && IsOpenDmxSelected)
            OutputInfo = FtdiDriverMessage ?? "Sortie OpenDMX indisponible";
        else if (OutputDevices.Count == 0)
            OutputInfo = _ftdiDriverStatus.GetDetectionHint() ?? "Aucun périphérique DMX détecté";
        else
        {
            var hint = IsOpenDmxSelected ? _ftdiDriverStatus.GetDetectionHint() : null;
            OutputInfo = hint is not null
                ? $"{SelectedOutputDevice?.Description} — {hint}"
                : SelectedOutputDevice?.Description ?? "Aucun périphérique";
        }
    }

    private bool IsOpenDmxOutputAvailable()
    {
        if (!IsOpenDmxSelected)
            return true;

        return ((OpenDmxOutput)_outputFactory.Create("OpenDMX")).IsDriverAvailable;
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

        SelectedOperationMode = FindOption(OperationModeOptions, s.OperationMode)
                              ?? OperationModeOptions[0];
        SelectedLogLevel = FindOption(LogLevelOptions, s.MinimumLogLevel)
                           ?? LogLevelOptions[3];
        _logger.MinimumLevel = SelectedLogLevel.Value;
        Console.LoadFrom(s);

        UpdateUniverseDisplay();

        foreach (var entry in _logger.GetRecentEntries(200))
            LogEntries.Add(entry);
    }

    public void SaveSettings() => PersistSettings();

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
            s.MinimumLogLevel = SelectedLogLevel?.Value ?? LogLevel.Info;
            Console.SaveTo(s);
        });
        _settings.Save();
    }

    private static LabeledOption<T>? FindOption<T>(IEnumerable<LabeledOption<T>> options, T value)
    {
        foreach (var option in options)
        {
            if (EqualityComparer<T>.Default.Equals(option.Value, value))
                return option;
        }

        return null;
    }

    private void UpdateUniverseDisplay() =>
        UniverseDisplay = $"{ArtNetNet}.{ArtNetSubNet}.{ArtNetUniverse}";

    private void UpdateFtdiDriverStatus()
    {
        if (FtdiUsbDiagnostics.FindFtdiComPorts().Count > 0)
            _ftdiDriverStatus.ResetProbe();
        else
            _ftdiDriverStatus.Probe();

        if (IsOpenDmxSelected)
        {
            var openDmx = (OpenDmxOutput)_outputFactory.Create("OpenDMX");
            FtdiDriverAvailable = openDmx.IsDriverAvailable;
            FtdiDriverMessage = openDmx.DriverUnavailableMessage;
        }
        else
        {
            FtdiDriverAvailable = _ftdiDriverStatus.IsAvailable;
            FtdiDriverMessage = _ftdiDriverStatus.UnavailableMessage;
        }

        UpdateFtdiWarningVisibility();
    }

    private void UpdateFtdiWarningVisibility() =>
        ShowFtdiWarning = !FtdiDriverAvailable && (IsOpenDmxSelected || IsBridgeMode);

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        FlushPendingLogEntries();
        Console.RefreshSummary();

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
        LastPacketDisplay = monitor.LastPacketMs < 0 ? "—" : $"{monitor.LastPacketMs:F0} ms";
        MonitorSource = monitor.Source;

        if (IsBridgeRunning)
            OutputInfo = stats.OutputDescription ?? OutputInfo;

        MonitorSummary = $"Univers {monitor.Universe}\n" +
                         $"FPS Art-Net : {monitor.ArtNetFps:F1}\n" +
                         $"FPS DMX : {monitor.DmxFps:F1}\n" +
                         $"Source : {monitor.Source}\n" +
                         $"Paquets : {monitor.PacketsReceived:N0}\n" +
                         $"Dernier paquet : {(monitor.LastPacketMs < 0 ? "—" : $"{monitor.LastPacketMs:F0} ms")}";
    }

    private void OnLogEntryAdded(object? sender, LogEntry entry) => _pendingLogEntries.Enqueue(entry);

    private void FlushPendingLogEntries()
    {
        while (_pendingLogEntries.TryDequeue(out var entry))
            AppendLogEntry(entry);
    }

    private void AppendLogEntry(LogEntry entry)
    {
        LogEntries.Add(entry);
        while (LogEntries.Count > 500)
            LogEntries.RemoveAt(0);
    }

    public void Dispose()
    {
        SaveSettings();
        _uiTimer.Stop();
        _logger.EntryAdded -= OnLogEntryAdded;
    }
}
