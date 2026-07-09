using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Services.Dmx;

/// <summary>
/// Moteur DMX thread-safe avec double buffer et boucle dédiée (~44 Hz).
/// Aucune allocation dans la boucle de sortie.
/// </summary>
public sealed class DmxEngine : IDmxEngine
{
    public const int ChannelCount = 512;

    private readonly IFtdiOutputService _ftdi;
    private readonly ILoggingService _logger;

    private readonly byte[] _writeBuffer = new byte[ChannelCount];
    private readonly byte[] _readBuffer = new byte[ChannelCount];
    private readonly object _bufferLock = new();

    private CancellationTokenSource? _cts;
    private Thread? _outputThread;
    private volatile bool _isRunning;

    private int _refreshHz = 44;
    private long _framesSent;
    private double _currentFps;
    private long _fpsCounter;
    private DateTime _fpsWindowStart = DateTime.UtcNow;

    public DmxEngine(IFtdiOutputService ftdi, ILoggingService logger)
    {
        _ftdi = ftdi;
        _logger = logger;
    }

    public bool IsRunning => _isRunning;

    public int RefreshHz
    {
        get => _refreshHz;
        set => _refreshHz = Math.Clamp(value, 1, 60);
    }

    public long FramesSent => Interlocked.Read(ref _framesSent);
    public double CurrentFps => _currentFps;

    public ReadOnlySpan<byte> GetChannelSnapshot()
    {
        lock (_bufferLock)
        {
            _writeBuffer.AsSpan().CopyTo(_readBuffer);
            return _readBuffer;
        }
    }

    public void ApplyArtNetData(ReadOnlySpan<byte> data, int length, int startChannel = 1)
    {
        if (length <= 0)
            return;

        lock (_bufferLock)
        {
            var startIndex = Math.Clamp(startChannel - 1, 0, ChannelCount - 1);
            var maxCopy = Math.Min(length, ChannelCount - startIndex);
            data.Slice(0, maxCopy).CopyTo(_writeBuffer.AsSpan(startIndex));
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isRunning = true;
        _fpsWindowStart = DateTime.UtcNow;
        _fpsCounter = 0;

        _outputThread = new Thread(OutputLoop)
        {
            Name = "DMX-Output",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _outputThread.Start(_cts.Token);

        _logger.Info($"Moteur DMX démarré ({RefreshHz} Hz).", nameof(DmxEngine));
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_isRunning)
            return Task.CompletedTask;

        _isRunning = false;
        _cts?.Cancel();

        if (_outputThread is not null && _outputThread.IsAlive)
        {
            _outputThread.Join(TimeSpan.FromSeconds(2));
        }

        _cts?.Dispose();
        _cts = null;
        _outputThread = null;

        _logger.Info("Moteur DMX arrêté.", nameof(DmxEngine));
        return Task.CompletedTask;
    }

    private void OutputLoop(object? state)
    {
        var token = state is CancellationToken ct ? ct : CancellationToken.None;
        var localFrame = new byte[ChannelCount];

        while (_isRunning && !token.IsCancellationRequested)
        {
            var frameStart = DateTime.UtcNow;

            lock (_bufferLock)
            {
                _writeBuffer.AsSpan().CopyTo(localFrame);
            }

            if (_ftdi.IsConnected)
            {
                _ftdi.SendFrame(localFrame);
                Interlocked.Increment(ref _framesSent);
                Interlocked.Increment(ref _fpsCounter);
            }

            UpdateFps();

            var targetMs = 1000.0 / RefreshHz;
            var elapsedMs = (DateTime.UtcNow - frameStart).TotalMilliseconds;
            var sleepMs = (int)Math.Max(0, targetMs - elapsedMs);

            if (sleepMs > 0)
                Thread.Sleep(sleepMs);
        }
    }

    private void UpdateFps()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _fpsWindowStart).TotalSeconds;
        if (elapsed < 1.0)
            return;

        _currentFps = Interlocked.Exchange(ref _fpsCounter, 0) / elapsed;
        _fpsWindowStart = now;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
