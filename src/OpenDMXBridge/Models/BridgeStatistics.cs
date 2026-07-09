namespace OpenDMXBridge.Models;

/// <summary>
/// Statistiques temps réel du bridge (thread-safe via snapshots).
/// </summary>
public sealed class BridgeStatistics
{
    public bool ArtNetListening { get; init; }
    public bool FtdiConnected { get; init; }
    public double DmxFps { get; init; }
    public long PacketsReceived { get; init; }
    public long PacketsSent { get; init; }
    public long InvalidPackets { get; init; }
    public DateTimeOffset LastArtNetPacket { get; init; }
    public string? FtdiDescription { get; init; }
    public string? LastError { get; init; }
}
