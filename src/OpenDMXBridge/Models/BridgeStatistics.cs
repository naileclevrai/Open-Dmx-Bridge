namespace OpenDMXBridge.Models;

/// <summary>
/// Statistiques temps réel du bridge (thread-safe via snapshots).
/// </summary>
public sealed class BridgeStatistics
{
    public BridgeOperationMode Mode { get; init; }
    public bool ArtNetListening { get; init; }
    public bool DmxOutputConnected { get; init; }
    public double DmxFps { get; init; }
    public double ArtNetFps { get; init; }
    public long PacketsReceived { get; init; }
    public long PacketsSent { get; init; }
    public long InvalidPackets { get; init; }
    public long LostSequences { get; init; }
    public long OutOfOrderPackets { get; init; }
    public double LastPacketMs { get; init; }
    public bool ArtNetTimedOut { get; init; }
    public DateTimeOffset LastArtNetPacket { get; init; }
    public string? OutputDescription { get; init; }
    public string? LastSource { get; init; }
    public string? LastError { get; init; }
    public UniverseId ActiveUniverse { get; init; }
}
