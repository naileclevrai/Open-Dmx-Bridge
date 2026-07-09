using OpenDMXBridge.Models;

namespace OpenDMXBridge.Services.Contracts;

public interface IFtdiOutputService : IAsyncDisposable
{
    bool IsConnected { get; }
    string? DeviceDescription { get; }

    IReadOnlyList<FtdiDeviceInfo> EnumerateDevices();
    Task<bool> ConnectAsync(int deviceIndex, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    void SendFrame(ReadOnlySpan<byte> dmxData);
}
