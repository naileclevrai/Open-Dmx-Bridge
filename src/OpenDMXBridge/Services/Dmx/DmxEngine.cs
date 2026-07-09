using System.Collections.Concurrent;
using System.Diagnostics;
using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;
using OpenDMXBridge.Services.Dmx;

namespace OpenDMXBridge.Services.Dmx;

/// <summary>
/// Moteur DMX : multi-univers, double buffer atomique, horloge précise, zéro allocation en boucle.
/// </summary>
public sealed class DmxEngine : IDmxEngine
{
    private readonly ILoggingService _logger;
    private readonly ConcurrentDictionary<UniverseId, UniverseBuffer> _universes = new();
    private readonly byte[] _outputFrame = new byte[UniverseBuffer.SlotCount];
    private readonly PrecisePeriodicLoop _clock = new(44);

    private volatile IDmxOutput? _output;
    private CancellationTokenSource? _cts;
    private Thread? _outputThread;
    private volatile bool _isRunning;

    private int _refreshHz = 44;
    private long _framesSent;
    private double _currentFps;
    private long _fpsCounter;
    private long _fpsWindowStartTimestamp;
    private UniverseId _activeUniverse;

    public DmxEngine(ILoggingService logger)
    {
        _logger = logger;
        _fpsWindowStartTimestamp = Stopwatch.GetTimestamp();
    }

    public bool IsRunning => _isRunning;

    public int RefreshHz
    {
        get => _refreshHz;
        set
        {
            _refreshHz = Math.Clamp(value, 1, 60);
            _clock.SetFrequency(_refreshHz);
            _clock.Reset();
        }
    }

    public long FramesSent => Interlocked.Read(ref _framesSent);
    public double CurrentFps => _currentFps;

    public UniverseId ActiveUniverse
    {
        get => _activeUniverse;
        set => _activeUniverse = value;
    }

    public void SetOutput(IDmxOutput output) => _output = output;

    public void CopyActiveUniverseSnapshot(Span<byte> destination)
    {
        if (_universes.TryGetValue(_activeUniverse, out var buffer))
            buffer.CopySnapshot(destination);
        else
            destination.Clear();
    }

    public void ApplyArtNetPatch(UniverseId universe, ReadOnlySpan<byte> data, int startChannel = 1)
    {
        if (data.Length == 0)
            return;

        var buffer = _universes.GetOrAdd(universe, static _ => new UniverseBuffer());
        buffer.ApplyPatch(data, startChannel);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isRunning = true;
        _fpsCounter = 0;
        _fpsWindowStartTimestamp = Stopwatch.GetTimestamp();
        _clock.SetFrequency(RefreshHz);
        _clock.Reset();

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
            _outputThread.Join(TimeSpan.FromSeconds(2));

        _cts?.Dispose();
        _cts = null;
        _outputThread = null;

        _logger.Info("Moteur DMX arrêté.", nameof(DmxEngine));
        return Task.CompletedTask;
    }

    private void OutputLoop(object? state)
    {
        var token = state is CancellationToken ct ? ct : CancellationToken.None;
        _clock.Start();

        while (_isRunning && !token.IsCancellationRequested)
        {
            var output = _output;
            if (output is not null && output.IsConnected)
            {
                if (_universes.TryGetValue(_activeUniverse, out var buffer))
                    buffer.CopySnapshot(_outputFrame);
                else
                    _outputFrame.AsSpan().Clear();

                output.SendFrame(_outputFrame);
                Interlocked.Increment(ref _framesSent);
                Interlocked.Increment(ref _fpsCounter);
            }

            UpdateFps();
            _clock.WaitForNextTick();
        }
    }

    private void UpdateFps()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _fpsWindowStartTimestamp) / (double)Stopwatch.Frequency;
        if (elapsed < 1.0)
            return;

        _currentFps = Interlocked.Exchange(ref _fpsCounter, 0) / elapsed;
        _fpsWindowStartTimestamp = now;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
