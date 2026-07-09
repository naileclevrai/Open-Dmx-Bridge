namespace OpenDMXBridge.Services.Contracts;

public interface IDmxOutput : IAsyncDisposable
{
    string OutputType { get; }
    string DisplayName { get; }
    bool IsConnected { get; }
    bool SupportsAutoReconnect { get; }
    long FramesSent { get; }
    string? DeviceDescription { get; }

    IReadOnlyList<Models.DmxOutputDevice> EnumerateDevices();
    Task<bool> ConnectAsync(Models.DmxOutputDevice device, CancellationToken cancellationToken = default);
    Task DisconnectAsync();

    /// <summary>512 slots DMX (canaux 1-512). Aucune allocation autorisée dans l'implémentation.</summary>
    void SendFrame(ReadOnlySpan<byte> channels);
}

public interface IDmxOutputFactory
{
    IReadOnlyList<string> AvailableOutputTypes { get; }
    IDmxOutput Create(string outputType);
}
