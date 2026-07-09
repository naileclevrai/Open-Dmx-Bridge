using System.Diagnostics;

namespace OpenDMXBridge.Services.Dmx;

/// <summary>
/// Horloge périodique avec correction de dérive (Stopwatch), sans Task.Delay fixe.
/// </summary>
internal sealed class PrecisePeriodicLoop
{
    private double _intervalMs;
    private readonly Stopwatch _stopwatch = new();
    private long _tickCount;

    public PrecisePeriodicLoop(double frequencyHz) => SetFrequency(frequencyHz);

    public void SetFrequency(double frequencyHz)
    {
        if (frequencyHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(frequencyHz));

        _intervalMs = 1000.0 / frequencyHz;
    }

    public void Start() => _stopwatch.Restart();

    public void WaitForNextTick()
    {
        _tickCount++;
        var targetMs = _intervalMs * _tickCount;

        var remaining = targetMs - _stopwatch.Elapsed.TotalMilliseconds;
        if (remaining > 2.0)
            Thread.Sleep((int)(remaining - 1.0));

        while (_stopwatch.Elapsed.TotalMilliseconds < targetMs)
            Thread.SpinWait(20);
    }

    public void Reset()
    {
        _tickCount = 0;
        _stopwatch.Restart();
    }
}

