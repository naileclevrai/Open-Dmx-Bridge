using OpenDMXBridge.Services.Contracts;
using OpenDMXBridge.Services.Ftdi;

namespace OpenDMXBridge.Services;

public sealed class FtdiDriverStatus : IFtdiDriverStatus
{
    public bool IsAvailable
    {
        get
        {
            if (FtdiUsbDiagnostics.FindFtdiComPorts().Count > 0)
                return true;

            FtdiNative.EnsureProbed();
            return FtdiNative.IsAvailable();
        }
    }

    public string? UnavailableMessage
    {
        get
        {
            if (FtdiUsbDiagnostics.FindFtdiComPorts().Count > 0)
                return null;

            FtdiNative.EnsureProbed();
            return FtdiNative.UnavailableReason;
        }
    }

    public string? GetDetectionHint()
    {
        var comPorts = FtdiUsbDiagnostics.FindFtdiComPorts();
        if (comPorts.Count > 0)
            return null;

        FtdiNative.EnsureProbed();

        if (!FtdiNative.IsAvailable())
            return UnavailableMessage;

        var count = FtdiNative.GetD2xxDeviceCount();
        if (count > 0)
            return null;

        return FtdiUsbDiagnostics.BuildVcpOnlyHint(comPorts)
               ?? "FTD2XX.dll OK mais aucun périphérique D2XX. Branchez l'Enttec Open DMX USB et fermez les apps utilisant le port COM.";
    }

    public void Probe()
    {
        if (FtdiUsbDiagnostics.FindFtdiComPorts().Count == 0)
            FtdiNative.EnsureProbed();
    }

    public void ResetProbe() => FtdiNative.ResetProbe();
}
