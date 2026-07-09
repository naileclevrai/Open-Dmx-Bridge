using OpenDMXBridge.Models;

namespace OpenDMXBridge.Services.Contracts;

public interface INetworkService
{
    bool IsListening { get; }
    ArtNetUniverse TargetUniverse { get; }

    event EventHandler? PacketReceived;
    event EventHandler<string>? ListenStateChanged;

    IReadOnlyList<NetworkAdapterInfo> GetNetworkAdapters();
    void SetTargetUniverse(ArtNetUniverse universe);
    Task StartAsync(string? adapterId, CancellationToken cancellationToken = default);
    Task StopAsync();
}
