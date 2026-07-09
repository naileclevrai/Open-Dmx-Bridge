using OpenDMXBridge.Services.Contracts;
using OpenDMXBridge.Services.Ftdi;

namespace OpenDMXBridge.Services;

public sealed class FtdiDriverStatus : IFtdiDriverStatus
{
    public bool IsAvailable
    {
        get
        {
            FtdiNative.EnsureProbed();
            return FtdiNative.IsAvailable();
        }
    }

    public string? UnavailableMessage
    {
        get
        {
            FtdiNative.EnsureProbed();
            return FtdiNative.UnavailableReason;
        }
    }

    public void Probe() => FtdiNative.EnsureProbed();
}
