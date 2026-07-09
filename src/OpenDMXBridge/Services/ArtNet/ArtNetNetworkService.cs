using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Services.ArtNet;

/// <summary>
/// Réception Art-Net UDP sur le port 6454. Aucune dépendance UI.
/// </summary>
public sealed class ArtNetNetworkService : INetworkService, IAsyncDisposable
{
    private readonly ILoggingService _logger;
    private readonly IDmxEngine _dmxEngine;
    private readonly object _stateLock = new();

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private ArtNetUniverse _targetUniverse = new(0, 0, 0);
    private string? _adapterId;

    private long _packetsReceived;
    private long _invalidPackets;
    private DateTimeOffset _lastPacket = DateTimeOffset.MinValue;

    public ArtNetNetworkService(ILoggingService logger, IDmxEngine dmxEngine)
    {
        _logger = logger;
        _dmxEngine = dmxEngine;
    }

    public bool IsListening { get; private set; }

    public ArtNetUniverse TargetUniverse
    {
        get
        {
            lock (_stateLock)
                return _targetUniverse;
        }
    }

    public long PacketsReceived => Interlocked.Read(ref _packetsReceived);
    public long InvalidPackets => Interlocked.Read(ref _invalidPackets);
    public DateTimeOffset LastPacket => _lastPacket;

    public event EventHandler? PacketReceived;
    public event EventHandler<string>? ListenStateChanged;

    public IReadOnlyList<NetworkAdapterInfo> GetNetworkAdapters()
    {
        var adapters = new List<NetworkAdapterInfo>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Tunnel)
                continue;

            var ipProps = nic.GetIPProperties();
            foreach (var uni in ipProps.UnicastAddresses)
            {
                if (uni.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var id = $"{nic.Id}|{uni.Address}";
                adapters.Add(new NetworkAdapterInfo(
                    id,
                    nic.Name,
                    uni.Address.ToString(),
                    true));
            }
        }

        return adapters
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void SetTargetUniverse(ArtNetUniverse universe)
    {
        lock (_stateLock)
            _targetUniverse = universe;
    }

    public async Task StartAsync(string? adapterId, CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);

        lock (_stateLock)
            _adapterId = adapterId;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;

        try
        {
            var bindAddress = IPAddress.Any;
            if (!string.IsNullOrWhiteSpace(adapterId))
            {
                var ip = adapterId.Split('|').LastOrDefault();
                if (IPAddress.TryParse(ip, out var bindIp))
                    bindAddress = bindIp;
            }

            var localEp = new IPEndPoint(bindAddress, ArtNetProtocol.Port);
            _udpClient = new UdpClient(AddressFamily.InterNetwork)
            {
                ExclusiveAddressUse = false
            };
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(localEp);

            IsListening = true;
            ListenStateChanged?.Invoke(this, "Écoute Art-Net active.");
            _logger.Info($"Art-Net en écoute sur UDP {ArtNetProtocol.Port}.", nameof(ArtNetNetworkService));

            _receiveTask = Task.Run(() => ReceiveLoopAsync(token), token);
        }
        catch (Exception ex)
        {
            IsListening = false;
            _logger.Error($"Impossible de démarrer Art-Net : {ex.Message}", nameof(ArtNetNetworkService));
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync()
    {
        IsListening = false;

        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _udpClient?.Dispose();
        _udpClient = null;
        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;

        ListenStateChanged?.Invoke(this, "Écoute Art-Net arrêtée.");
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (_udpClient is null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                ProcessPacket(result.Buffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Erreur réception UDP : {ex.Message}", nameof(ArtNetNetworkService));
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void ProcessPacket(ReadOnlySpan<byte> buffer)
    {
        if (!ArtNetProtocol.TryParseArtDmx(buffer, out var net, out var subnet, out var universe, out var dmxData, out var length))
        {
            Interlocked.Increment(ref _invalidPackets);
            return;
        }

        ArtNetUniverse target;
        lock (_stateLock)
            target = _targetUniverse;

        var incoming = new ArtNetUniverse(net, subnet, universe);
        if (incoming != target)
            return;

        _dmxEngine.ApplyArtNetData(dmxData, length);
        Interlocked.Increment(ref _packetsReceived);
        _lastPacket = DateTimeOffset.Now;
        PacketReceived?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
