using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Services.Outputs;

/// <summary>Placeholder — implémentation future.</summary>
public abstract class StubDmxOutput : IDmxOutput
{
    private readonly ILoggingService _logger;

    protected StubDmxOutput(ILoggingService logger, string outputType, string displayName)
    {
        _logger = logger;
        OutputType = outputType;
        DisplayName = displayName;
    }

    public string OutputType { get; }
    public string DisplayName { get; }
    public bool IsConnected => false;
    public bool SupportsAutoReconnect => false;
    public long FramesSent => 0;
    public string? DeviceDescription => null;

    public IReadOnlyList<DmxOutputDevice> EnumerateDevices() => Array.Empty<DmxOutputDevice>();

    public Task<bool> ConnectAsync(DmxOutputDevice device, CancellationToken cancellationToken = default)
    {
        _logger.Info($"{DisplayName} — non implémenté (prévu dans une version future).", OutputType);
        return Task.FromResult(false);
    }

    public Task DisconnectAsync() => Task.CompletedTask;
    public void SendFrame(ReadOnlySpan<byte> channels) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class EnttecProOutput(ILoggingService logger)
    : StubDmxOutput(logger, "EnttecPro", "Enttec USB Pro");

public sealed class DmxKingOutput(ILoggingService logger)
    : StubDmxOutput(logger, "DMXKing", "DMXKing");

public sealed class SacnOutput(ILoggingService logger)
    : StubDmxOutput(logger, "sACN", "sACN (E1.31) sortie");

public sealed class ArtNetOutput(ILoggingService logger)
    : StubDmxOutput(logger, "ArtNet", "Art-Net sortie");
