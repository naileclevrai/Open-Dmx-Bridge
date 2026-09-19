namespace OpenDMXBridge.Models;

public sealed record DmxOutputDevice(string Id, string Description, string? SerialNumber, int NativeIndex = -1)
{
    public override string ToString() => Description;
}
