using OpenDMXBridge.Models;

namespace OpenDMXBridge.Services.Contracts;

/// <summary>
/// Orchestrateur du bridge : relie réseau, moteur DMX et sortie FTDI.
/// </summary>
public interface IBridgeOrchestrator : IAsyncDisposable
{
    bool IsRunning { get; }
    BridgeStatistics GetStatistics();

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
