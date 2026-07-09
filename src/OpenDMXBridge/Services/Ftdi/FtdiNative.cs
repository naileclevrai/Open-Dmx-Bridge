using System.Runtime.InteropServices;

namespace OpenDMXBridge.Services.Ftdi;

/// <summary>
/// Bindings P/Invoke vers FTD2XX.dll (FTDI D2XX driver).
/// </summary>
internal static class FtdiNative
{
    private const string DllName = "FTD2XX.dll";

    public const uint OpenByIndex = 0x00000004;
    public const byte Bits8 = 8;
    public const byte StopBits2 = 2;
    public const byte ParityNone = 0;
    public const ushort FlowNone = 0;
    public const byte PurgeRx = 1;
    public const byte PurgeTx = 2;

    public const uint BaudDmx = 250_000;
    public const uint BaudBreak = 45_454;

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_CreateDeviceInfoList(ref uint numDevs);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern int FT_GetDeviceInfoDetail(
        uint index,
        ref uint flags,
        ref uint type,
        ref uint id,
        ref uint locId,
        byte[] serial,
        byte[] description,
        ref IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_Open(int deviceNumber, out IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_Close(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_SetBaudRate(IntPtr handle, uint baudRate);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_SetDataCharacteristics(IntPtr handle, byte wordLength, byte stopBits, byte parity);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_SetFlowControl(IntPtr handle, ushort flowControl, byte xon, byte xoff);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_SetLatencyTimer(IntPtr handle, byte latency);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_SetBreakOn(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_SetBreakOff(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_Purge(IntPtr handle, uint mask);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_Write(IntPtr handle, byte[] buffer, int length, ref uint bytesWritten);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_GetStatus(IntPtr handle, ref uint rxBytes, ref uint txBytes, ref uint eventStatus);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern int FT_ResetDevice(IntPtr handle);

    public static bool IsAvailable()
    {
        try
        {
            uint count = 0;
            return FT_CreateDeviceInfoList(ref count) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    public static string GetStatusMessage(int status) => status switch
    {
        0 => "OK",
        1 => "Handle invalide",
        2 => "Périphérique non trouvé",
        3 => "Périphérique non ouvert",
        4 => "Erreur IO",
        5 => "Paramètre invalide",
        _ => $"Code FTDI {status}"
    };
}
