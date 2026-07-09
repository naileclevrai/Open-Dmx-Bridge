using OpenDMXBridge.Models;
using OpenDMXBridge.Services.ArtNet;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Services;

/// <summary>
/// Relie les services réseau, DMX et FTDI. Point d'entrée unique pour l'UI.
/// </summary>
public sealed class BridgeOrchestrator : IBridgeOrchestrator
{
    private readonly ISettingsService _settings;
    private readonly ArtNetNetworkService _network;
    private readonly IDmxEngine _dmxEngine;
    private readonly IFtdiOutputService _ftdi;
    private readonly ILoggingService _logger;

    private bool _isRunning;
    private int _connectedDeviceIndex = -1;

    public BridgeOrchestrator(
        ISettingsService settings,
        INetworkService network,
        IDmxEngine dmxEngine,
        IFtdiOutputService ftdi,
        ILoggingService logger)
    {
        _settings = settings;
        _network = (ArtNetNetworkService)network;
        _dmxEngine = dmxEngine;
        _ftdi = ftdi;
        _logger = logger;
    }

    public bool IsRunning => _isRunning;

    public BridgeStatistics GetStatistics() => new()
    {
        ArtNetListening = _network.IsListening,
        FtdiConnected = _ftdi.IsConnected,
        DmxFps = _dmxEngine.CurrentFps,
        PacketsReceived = _network.PacketsReceived,
        PacketsSent = _ftdi is Ftdi.FtdiOutputService ftdiSvc ? ftdiSvc.PacketsSent : _dmxEngine.FramesSent,
        InvalidPackets = _network.InvalidPackets,
        LastArtNetPacket = _network.LastPacket,
        FtdiDescription = _ftdi.DeviceDescription
    };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return;

        var settings = _settings.Current;
        _dmxEngine.RefreshHz = settings.DmxRefreshHz;

        var universe = new ArtNetUniverse(settings.ArtNetNet, settings.ArtNetSubNet, settings.ArtNetUniverse);
        _network.SetTargetUniverse(universe);

        var devices = _ftdi.EnumerateDevices();
        if (!_ftdi.IsConnected)
        {
            if (devices.Count == 0)
            {
                _logger.Warning("Aucune interface FTDI détectée. Le bridge écoute Art-Net sans sortie DMX.", nameof(BridgeOrchestrator));
            }
            else
            {
                _connectedDeviceIndex = devices[0].Index;
                await _ftdi.ConnectAsync(_connectedDeviceIndex, cancellationToken).ConfigureAwait(false);
            }
        }

        await _dmxEngine.StartAsync(cancellationToken).ConfigureAwait(false);
        await _network.StartAsync(settings.SelectedNetworkAdapterId, cancellationToken).ConfigureAwait(false);

        _isRunning = true;
        _logger.Info($"Bridge démarré — univers {universe}.", nameof(BridgeOrchestrator));
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        await _network.StopAsync().ConfigureAwait(false);
        await _dmxEngine.StopAsync().ConfigureAwait(false);
        await _ftdi.DisconnectAsync().ConfigureAwait(false);

        _isRunning = false;
        _connectedDeviceIndex = -1;
        _logger.Info("Bridge arrêté.", nameof(BridgeOrchestrator));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);

        if (_dmxEngine is IAsyncDisposable dmxDisposable)
            await dmxDisposable.DisposeAsync().ConfigureAwait(false);

        if (_ftdi is IAsyncDisposable ftdiDisposable)
            await ftdiDisposable.DisposeAsync().ConfigureAwait(false);

        if (_network is IAsyncDisposable netDisposable)
            await netDisposable.DisposeAsync().ConfigureAwait(false);
    }
}
