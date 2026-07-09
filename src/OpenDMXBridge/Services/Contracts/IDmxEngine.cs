namespace OpenDMXBridge.Services.Contracts;

/// <summary>
/// Moteur DMX central : fusion des entrées, buffer 512 canaux, boucle de rafraîchissement.
/// </summary>
public interface IDmxEngine : IAsyncDisposable
{
    bool IsRunning { get; }
    int RefreshHz { get; set; }
    long FramesSent { get; }
    double CurrentFps { get; }

    ReadOnlySpan<byte> GetChannelSnapshot();
    void ApplyArtNetData(ReadOnlySpan<byte> data, int length, int startChannel = 1);

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
