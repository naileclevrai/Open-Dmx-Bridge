using OpenDMXBridge.Models;

namespace OpenDMXBridge.Services.Contracts;

/// <summary>
/// Moteur DMX central : multi-univers, buffer atomique, boucle de rafraîchissement précise.
/// </summary>
public interface IDmxEngine : IAsyncDisposable
{
    bool IsRunning { get; }
    int RefreshHz { get; set; }
    long FramesSent { get; }
    double CurrentFps { get; }
    UniverseId ActiveUniverse { get; set; }

    /// <summary>Micro console locale fusionnée dans la trame sortante.</summary>
    Services.Dmx.DmxConsoleLayer Console { get; }

    void CopyActiveUniverseSnapshot(Span<byte> destination);
    void ApplyArtNetPatch(UniverseId universe, ReadOnlySpan<byte> data, int startChannel = 1);

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
