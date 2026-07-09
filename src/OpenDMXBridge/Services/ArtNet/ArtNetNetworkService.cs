using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Services.ArtNet;

/// <summary>
/// Réception Art-Net UDP. Buffer réseau → double buffer DMX via le moteur (pas d'écriture directe depuis le thread UI).
/// </summary>
public sealed class ArtNetNetworkService : INetworkService, IAsyncDisposable
{
    private const double TimeoutSeconds = 2.0;

    private readonly ILoggingService _logger;
    private readonly IDmxEngine _dmxEngine;
    private readonly object _stateLock = new();
    private readonly byte[] _recvBuffer = new byte[1024];

    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private UniverseId _targetUniverse;
    private string? _adapterId;

    private long _packetsReceived;
    private long _invalidPackets;
    private long _lostSequences;
    private long _outOfOrderPackets;
    private long _artNetFpsCounter;
    private double _artNetFps;
    private long _fpsWindowStartTimestamp;
    private long _lastPacketTimestamp;
    private string _lastSource = "—";

    private bool _hasSequence;
    private byte _lastSequence;

    public ArtNetNetworkService(ILoggingService logger, IDmxEngine dmxEngine)
    {
        _logger = logger;
        _dmxEngine = dmxEngine;
        _fpsWindowStartTimestamp = Stopwatch.GetTimestamp();
    }

    public bool IsListening { get; private set; }

    public UniverseId TargetUniverse
    {
        get
        {
            lock (_stateLock)
                return _targetUniverse;
        }
    }

    public long PacketsReceived => Interlocked.Read(ref _packetsReceived);
    public long InvalidPackets => Interlocked.Read(ref _invalidPackets);
    public long LostSequences => Interlocked.Read(ref _lostSequences);
    public long OutOfOrderPackets => Interlocked.Read(ref _outOfOrderPackets);
    public double ArtNetFps => _artNetFps;
    public string LastSource => _lastSource;

    public event EventHandler? PacketReceived;
    public event EventHandler<string>? ListenStateChanged;

    public ArtNetMonitorSnapshot GetMonitorSnapshot()
    {
        var now = Stopwatch.GetTimestamp();
        var lastMs = _lastPacketTimestamp == 0
            ? -1.0
            : (now - _lastPacketTimestamp) * 1000.0 / Stopwatch.Frequency;

        UniverseId target;
        lock (_stateLock)
            target = _targetUniverse;

        return new ArtNetMonitorSnapshot
        {
            Universe = target,
            ArtNetFps = _artNetFps,
            DmxFps = _dmxEngine.CurrentFps,
            Source = _lastSource,
            PacketsReceived = PacketsReceived,
            LastPacketMs = lastMs,
            IsTimedOut = _lastPacketTimestamp != 0 && lastMs > TimeoutSeconds * 1000.0,
            LostSequences = LostSequences,
            OutOfOrderPackets = OutOfOrderPackets,
            InvalidPackets = InvalidPackets
        };
    }

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
                adapters.Add(new NetworkAdapterInfo(id, nic.Name, uni.Address.ToString(), true));
            }
        }

        adapters.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return adapters;
    }

    public void SetTargetUniverse(UniverseId universe)
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
                var parts = adapterId.Split('|');
                if (parts.Length > 0 && IPAddress.TryParse(parts[^1], out var bindIp))
                    bindAddress = bindIp;
            }

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.Bind(new IPEndPoint(bindAddress, ArtNetProtocol.Port));

            IsListening = true;
            _hasSequence = false;
            _fpsWindowStartTimestamp = Stopwatch.GetTimestamp();
            ListenStateChanged?.Invoke(this, "Écoute Art-Net active.");
            _logger.Info($"Art-Net en écoute sur UDP {ArtNetProtocol.Port}.", nameof(ArtNetNetworkService));

            _receiveTask = Task.Run(() => ReceiveLoop(token), token);
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
            await _cts.CancelAsync().ConfigureAwait(false);

        if (_receiveTask is not null)
        {
            try { await _receiveTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _socket?.Dispose();
        _socket = null;
        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;

        ListenStateChanged?.Invoke(this, "Écoute Art-Net arrêtée.");
    }

    private void ReceiveLoop(CancellationToken cancellationToken)
    {
        if (_socket is null)
            return;

        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var received = _socket.ReceiveFrom(_recvBuffer, SocketFlags.None, ref remote);
                if (received > 0)
                    ProcessPacket(_recvBuffer.AsSpan(0, received), remote);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Erreur réception UDP : {ex.Message}", nameof(ArtNetNetworkService));
            }
        }
    }

    private void ProcessPacket(ReadOnlySpan<byte> buffer, EndPoint remote)
    {
        if (!ArtNetProtocol.TryParseArtDmx(buffer, out var sequence, out var net, out var subnet, out var universe, out var dmxData, out var length))
        {
            Interlocked.Increment(ref _invalidPackets);
            return;
        }

        var incoming = UniverseId.FromArtNet(net, subnet, universe);

        UniverseId target;
        lock (_stateLock)
            target = _targetUniverse;

        if (incoming != target)
            return;

        TrackSequence(sequence);

        _dmxEngine.ApplyArtNetPatch(incoming, dmxData.Slice(0, length));

        if (remote is IPEndPoint ep)
            _lastSource = ep.Address.ToString();

        Interlocked.Increment(ref _packetsReceived);
        _lastPacketTimestamp = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _artNetFpsCounter);
        UpdateArtNetFps();

        PacketReceived?.Invoke(this, EventArgs.Empty);
    }

    private void TrackSequence(byte sequence)
    {
        if (sequence == 0)
            return;

        if (!_hasSequence)
        {
            _lastSequence = sequence;
            _hasSequence = true;
            return;
        }

        var expected = (byte)(_lastSequence + 1);
        if (sequence == expected || (_lastSequence == 255 && sequence == 1))
        {
            _lastSequence = sequence;
            return;
        }

        if (sequence < _lastSequence && _lastSequence - sequence < 128)
        {
            Interlocked.Increment(ref _outOfOrderPackets);
        }
        else
        {
            var lost = (sequence - _lastSequence - 1 + 256) % 256;
            if (lost > 0 && lost < 128)
                Interlocked.Add(ref _lostSequences, lost);
        }

        _lastSequence = sequence;
    }

    private void UpdateArtNetFps()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _fpsWindowStartTimestamp) / (double)Stopwatch.Frequency;
        if (elapsed < 1.0)
            return;

        _artNetFps = Interlocked.Exchange(ref _artNetFpsCounter, 0) / elapsed;
        _fpsWindowStartTimestamp = now;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
