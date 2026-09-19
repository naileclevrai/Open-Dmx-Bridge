using Microsoft.Win32;

namespace OpenDMXBridge.Services.Ftdi;

/// <summary>
/// Détecte les interfaces FTDI visibles en port COM (pilote VCP) pour aider au diagnostic D2XX.
/// </summary>
internal static class FtdiUsbDiagnostics
{
    public static IReadOnlyList<string> FindFtdiComPorts()
    {
        var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectFromSerialCommMap(ports);
        CollectFromFtdiEnum(ports);
        return ports.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CollectFromSerialCommMap(HashSet<string> ports)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key is null)
                return;

            foreach (var devicePath in key.GetValueNames())
            {
                if (!devicePath.Contains("FTDIBUS", StringComparison.OrdinalIgnoreCase)
                    && !devicePath.Contains("VID_0403", StringComparison.OrdinalIgnoreCase)
                    && !devicePath.Contains("VCP", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (key.GetValue(devicePath) is string comPort && !string.IsNullOrWhiteSpace(comPort))
                    ports.Add(comPort);
            }
        }
        catch
        {
            // ignore registry access issues
        }
    }

    private static void CollectFromFtdiEnum(HashSet<string> ports)
    {
        try
        {
            using var ftdiRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\FTDIBUS");
            if (ftdiRoot is null)
                return;

            foreach (var productId in ftdiRoot.GetSubKeyNames())
            {
                using var productKey = ftdiRoot.OpenSubKey(productId);
                if (productKey is null)
                    continue;

                foreach (var instanceId in productKey.GetSubKeyNames())
                {
                    using var instanceKey = productKey.OpenSubKey(instanceId);
                    using var paramsKey = instanceKey?.OpenSubKey("Device Parameters");
                    if (paramsKey?.GetValue("PortName") is string comPort && !string.IsNullOrWhiteSpace(comPort))
                        ports.Add(comPort);
                }
            }
        }
        catch
        {
            // ignore registry access issues
        }
    }

    public static string BuildPortBusyHint(string deviceLabel) =>
        $"Impossible d'ouvrir {deviceLabel} : le boîtier est déjà utilisé par un autre logiciel " +
        "(grandMA2 onPC, QLC+, Daslight, moniteur série…). Fermez-le, ou lancez OpenDMX Bridge avant ce logiciel, puis réessayez.";

    public static string? BuildVcpOnlyHint(IReadOnlyList<string> comPorts)
    {
        if (comPorts.Count == 0)
            return null;

        var portList = string.Join(", ", comPorts);
        return $"Interface FTDI détectée sur {portList}, mais invisible en D2XX. " +
               "Le pilote « USB Serial (VCP) » est actif — OpenDMX nécessite le pilote FTDI D2XX (64-bit). " +
               "Fermez QLC+, ArtNetToDMX ou tout logiciel utilisant le port COM, puis réinstallez le driver D2XX depuis ftdichip.com/drivers/d2xx-drivers/.";
    }
}
