using OpenDMXBridge.Models;

namespace OpenDMXBridge.Services.Contracts;

public interface IBridgeOrchestrator : IAsyncDisposable
{
    bool IsRunning { get; }
    BridgeOperationMode Mode { get; set; }
    BridgeStatistics GetStatistics();
    ArtNetMonitorSnapshot GetMonitorSnapshot();

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
