using OpenDMXBridge.Models;

namespace OpenDMXBridge.Services.Contracts;

public interface INetworkService
{
    bool IsListening { get; }
    UniverseId TargetUniverse { get; }
    ArtNetMonitorSnapshot GetMonitorSnapshot();

    event EventHandler? PacketReceived;
    event EventHandler<string>? ListenStateChanged;

    IReadOnlyList<NetworkAdapterInfo> GetNetworkAdapters();
    void SetTargetUniverse(UniverseId universe);
    Task StartAsync(string? adapterId, CancellationToken cancellationToken = default);
    Task StopAsync();
}
