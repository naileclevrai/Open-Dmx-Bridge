using System.Diagnostics;

namespace OpenDMXBridge.Services.Dmx;

/// <summary>
/// Timings DMX512 (ESTA E1.11) et mesure logicielle via QueryPerformanceCounter.
/// La validation finale doit être faite à l'oscilloscope / analyseur logique.
/// </summary>
public static class Dmx512Timing
{
    /// <summary>Break minimum selon DMX512 (µs).</summary>
    public const double MinBreakMicroseconds = 88;

    /// <summary>MAB minimum selon DMX512 (µs).</summary>
    public const double MinMabMicroseconds = 8;

    /// <summary>Valeurs nominales par défaut (à calibrer après mesure matérielle).</summary>
    public const double DefaultBreakMicroseconds = 100;

    public const double DefaultMabMicroseconds = 12;

    public const uint DmxBaudRate = 250_000;

    private static readonly double TicksPerMicrosecond = Stopwatch.Frequency / 1_000_000.0;

    public static void WaitMicroseconds(double microseconds)
    {
        if (microseconds <= 0)
            return;

        var target = Stopwatch.GetTimestamp() + (long)(microseconds * TicksPerMicrosecond);
        while (Stopwatch.GetTimestamp() < target)
            Thread.SpinWait(10);
    }

    public static double ElapsedMicroseconds(long startTimestamp, long endTimestamp) =>
        (endTimestamp - startTimestamp) / TicksPerMicrosecond;

    public readonly struct PhaseMeasurement(double microseconds, bool meetsSpec, string phaseName)
    {
        public double Microseconds { get; } = microseconds;
        public bool MeetsSpec { get; } = meetsSpec;
        public string PhaseName { get; } = phaseName;
    }

    public readonly struct FrameTimingMeasurement(PhaseMeasurement breakPhase, PhaseMeasurement mabPhase)
    {
        public PhaseMeasurement Break { get; } = breakPhase;
        public PhaseMeasurement Mab { get; } = mabPhase;

        public bool MeetsSpec => Break.MeetsSpec && Mab.MeetsSpec;
    }

    /// <summary>
    /// Mesure la durée réelle des phases break et MAB (attente CPU + latence API FTDI).
    /// Ne remplace pas une mesure oscilloscope sur la ligne DMX.
    /// </summary>
    public static FrameTimingMeasurement MeasureBreakAndMab(
        Action setBreakOn,
        Action setBreakOff,
        double breakMicroseconds,
        double mabMicroseconds)
    {
        setBreakOn();
        var breakStart = Stopwatch.GetTimestamp();
        WaitMicroseconds(breakMicroseconds);
        setBreakOff();
        var breakEnd = Stopwatch.GetTimestamp();

        var mabStart = breakEnd;
        WaitMicroseconds(mabMicroseconds);
        var mabEnd = Stopwatch.GetTimestamp();

        var breakMeasured = ElapsedMicroseconds(breakStart, breakEnd);
        var mabMeasured = ElapsedMicroseconds(mabStart, mabEnd);

        return new FrameTimingMeasurement(
            new PhaseMeasurement(breakMeasured, breakMeasured >= MinBreakMicroseconds, "Break"),
            new PhaseMeasurement(mabMeasured, mabMeasured >= MinMabMicroseconds, "MAB"));
    }
}
