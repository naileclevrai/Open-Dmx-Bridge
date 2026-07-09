using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Services.Outputs;

/// <summary>Sortie vide — mode analyse sans matériel.</summary>
public sealed class NullDmxOutput : IDmxOutput
{
    public string OutputType => "Null";
    public string DisplayName => "Aucune sortie (monitor)";
    public bool IsConnected => true;
    public bool SupportsAutoReconnect => false;
    public long FramesSent { get; private set; }
    public string? DeviceDescription => "Sortie désactivée";

    public IReadOnlyList<DmxOutputDevice> EnumerateDevices() =>
        [new DmxOutputDevice("null", "Aucune sortie", null)];

    public Task<bool> ConnectAsync(DmxOutputDevice device, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task DisconnectAsync()
    {
        FramesSent = 0;
        return Task.CompletedTask;
    }

    public void SendFrame(ReadOnlySpan<byte> channels) => FramesSent++;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
