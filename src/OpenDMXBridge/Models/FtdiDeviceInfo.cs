namespace OpenDMXBridge.Models;

public sealed record FtdiDeviceInfo(int Index, string Description, string SerialNumber, bool IsOpen);
