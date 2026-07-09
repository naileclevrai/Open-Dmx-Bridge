using OpenDMXBridge.Models;
using OpenDMXBridge.Services.ArtNet;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Services;

/// <summary>
/// Relie réseau, moteur DMX et sortie plugin. Supporte les modes Bridge et Monitor.
/// </summary>
public sealed class BridgeOrchestrator : IBridgeOrchestrator
{
    private readonly ISettingsService _settings;
    private readonly ArtNetNetworkService _network;
    private readonly Dmx.DmxEngine _dmxEngine;
    private readonly IDmxOutputFactory _outputFactory;
    private readonly ILoggingService _logger;

    private bool _isRunning;
    private BridgeOperationMode _mode = BridgeOperationMode.Bridge;
    private IDmxOutput? _activeOutput;

    public BridgeOrchestrator(
        ISettingsService settings,
        INetworkService network,
        IDmxEngine dmxEngine,
        IDmxOutputFactory outputFactory,
        ILoggingService logger)
    {
        _settings = settings;
        _network = (ArtNetNetworkService)network;
        _dmxEngine = (Dmx.DmxEngine)dmxEngine;
        _outputFactory = outputFactory;
        _logger = logger;
    }

    public bool IsRunning => _isRunning;

    public BridgeOperationMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    public BridgeStatistics GetStatistics()
    {
        var monitor = _network.GetMonitorSnapshot();
        var output = _activeOutput;

        return new BridgeStatistics
        {
            Mode = _mode,
            ArtNetListening = _network.IsListening,
            DmxOutputConnected = output?.IsConnected ?? false,
            DmxFps = _dmxEngine.CurrentFps,
            ArtNetFps = monitor.ArtNetFps,
            PacketsReceived = monitor.PacketsReceived,
            PacketsSent = output?.FramesSent ?? 0,
            InvalidPackets = monitor.InvalidPackets,
            LostSequences = monitor.LostSequences,
            OutOfOrderPackets = monitor.OutOfOrderPackets,
            LastPacketMs = monitor.LastPacketMs,
            ArtNetTimedOut = monitor.IsTimedOut,
            LastArtNetPacket = DateTimeOffset.Now.AddMilliseconds(-monitor.LastPacketMs),
            OutputDescription = output?.DeviceDescription,
            LastSource = monitor.Source,
            ActiveUniverse = monitor.Universe
        };
    }

    public ArtNetMonitorSnapshot GetMonitorSnapshot() => _network.GetMonitorSnapshot();

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return;

        var settings = _settings.Current;
        _dmxEngine.RefreshHz = settings.DmxRefreshHz;

        var universe = new UniverseId(settings.ArtNetNet, settings.ArtNetSubNet, settings.ArtNetUniverse);
        _dmxEngine.ActiveUniverse = universe;
        _network.SetTargetUniverse(universe);

        var outputType = _mode == BridgeOperationMode.Monitor
            ? "Null"
            : settings.OutputType;

        _activeOutput = _outputFactory.Create(outputType);
        _dmxEngine.SetOutput(_activeOutput);

        if (_mode == BridgeOperationMode.Bridge && _activeOutput.SupportsAutoReconnect)
        {
            var devices = _activeOutput.EnumerateDevices();
            if (devices.Count == 0)
            {
                _logger.Warning("Aucune interface DMX détectée — écoute Art-Net sans sortie.", nameof(BridgeOrchestrator));
            }
            else if (!_activeOutput.IsConnected)
            {
                var device = ResolveDevice(devices, settings.OutputDeviceId) ?? devices[0];
                await _activeOutput.ConnectAsync(device, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (_mode == BridgeOperationMode.Monitor)
        {
            await _activeOutput.ConnectAsync(_activeOutput.EnumerateDevices()[0], cancellationToken).ConfigureAwait(false);
        }

        await _dmxEngine.StartAsync(cancellationToken).ConfigureAwait(false);
        await _network.StartAsync(settings.SelectedNetworkAdapterId, cancellationToken).ConfigureAwait(false);

        _isRunning = true;
        _logger.Info($"Bridge démarré [{_mode}] — univers {universe}.", nameof(BridgeOrchestrator));
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        await _network.StopAsync().ConfigureAwait(false);
        await _dmxEngine.StopAsync().ConfigureAwait(false);

        if (_activeOutput is not null)
            await _activeOutput.DisconnectAsync().ConfigureAwait(false);

        _activeOutput = null;
        _isRunning = false;
        _logger.Info("Bridge arrêté.", nameof(BridgeOrchestrator));
    }

    private static DmxOutputDevice? ResolveDevice(IReadOnlyList<DmxOutputDevice> devices, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        for (var i = 0; i < devices.Count; i++)
        {
            if (devices[i].Id == deviceId)
                return devices[i];
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);

        if (_dmxEngine is IAsyncDisposable dmxDisposable)
            await dmxDisposable.DisposeAsync().ConfigureAwait(false);

        if (_network is IAsyncDisposable netDisposable)
            await netDisposable.DisposeAsync().ConfigureAwait(false);
    }
}
