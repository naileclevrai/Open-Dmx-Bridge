namespace OpenDMXBridge.Models;

/// <summary>Instantané mode analyse / monitoring réseau.</summary>
public sealed class ArtNetMonitorSnapshot
{
    public UniverseId Universe { get; init; }
    public double ArtNetFps { get; init; }
    public double DmxFps { get; init; }
    public string Source { get; init; } = "—";
    public long PacketsReceived { get; init; }
    public double LastPacketMs { get; init; }
    public bool IsTimedOut { get; init; }
    public long LostSequences { get; init; }
    public long OutOfOrderPackets { get; init; }
    public long InvalidPackets { get; init; }
}
