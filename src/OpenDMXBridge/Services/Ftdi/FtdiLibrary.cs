using System.Runtime.InteropServices;

namespace OpenDMXBridge.Services.Ftdi;

/// <summary>
/// Chargement dynamique de FTD2XX.dll via NativeLibrary.
/// Aucun DllImport statique — l'application démarre même si la DLL est absente.
/// </summary>
internal sealed class FtdiLibrary
{
    private static readonly Lazy<FtdiLibrary> LazyInstance = new(static () => new FtdiLibrary());

    private readonly object _lock = new();
    private IntPtr _module;
    private string? _unavailableReason;
    private bool _probeAttempted;

    private FT_CreateDeviceInfoListDelegate? _createDeviceInfoList;
    private FT_GetDeviceInfoDetailDelegate? _getDeviceInfoDetail;
    private FT_OpenDelegate? _open;
    private FT_CloseDelegate? _close;
    private FT_SetBaudRateDelegate? _setBaudRate;
    private FT_SetDataCharacteristicsDelegate? _setDataCharacteristics;
    private FT_SetFlowControlDelegate? _setFlowControl;
    private FT_SetLatencyTimerDelegate? _setLatencyTimer;
    private FT_SetBreakOnDelegate? _setBreakOn;
    private FT_SetBreakOffDelegate? _setBreakOff;
    private FT_PurgeDelegate? _purge;
    private FT_WriteDelegate? _write;
    private FT_GetStatusDelegate? _getStatus;
    private FT_ResetDeviceDelegate? _resetDevice;

    public static FtdiLibrary Instance => LazyInstance.Value;

    public bool IsAvailable
    {
        get
        {
            EnsureProbed();
            return _module != IntPtr.Zero;
        }
    }

    public string? UnavailableReason
    {
        get
        {
            EnsureProbed();
            return _unavailableReason;
        }
    }

    public void EnsureProbed() => TryLoad();

    private bool TryLoad()
    {
        lock (_lock)
        {
            if (_module != IntPtr.Zero)
                return true;

            if (_probeAttempted && _unavailableReason is not null)
                return false;

            _probeAttempted = true;

            if (!NativeLibrary.TryLoad("FTD2XX.dll", typeof(FtdiLibrary).Assembly, DllImportSearchPath.AssemblyDirectory, out _module))
            {
                if (!NativeLibrary.TryLoad("FTD2XX.dll", out _module))
                {
                    _unavailableReason =
                        "FTD2XX.dll introuvable. Installez le pilote FTDI D2XX (x64) " +
                        "ou copiez FTD2XX.dll à côté de OpenDMXBridge.exe. " +
                        "Le mode Monitor reste disponible.";
                    return false;
                }
            }

            try
            {
                _createDeviceInfoList = GetDelegate<FT_CreateDeviceInfoListDelegate>("FT_CreateDeviceInfoList");
                _getDeviceInfoDetail = GetDelegate<FT_GetDeviceInfoDetailDelegate>("FT_GetDeviceInfoDetail");
                _open = GetDelegate<FT_OpenDelegate>("FT_Open");
                _close = GetDelegate<FT_CloseDelegate>("FT_Close");
                _setBaudRate = GetDelegate<FT_SetBaudRateDelegate>("FT_SetBaudRate");
                _setDataCharacteristics = GetDelegate<FT_SetDataCharacteristicsDelegate>("FT_SetDataCharacteristics");
                _setFlowControl = GetDelegate<FT_SetFlowControlDelegate>("FT_SetFlowControl");
                _setLatencyTimer = GetDelegate<FT_SetLatencyTimerDelegate>("FT_SetLatencyTimer");
                _setBreakOn = GetDelegate<FT_SetBreakOnDelegate>("FT_SetBreakOn");
                _setBreakOff = GetDelegate<FT_SetBreakOffDelegate>("FT_SetBreakOff");
                _purge = GetDelegate<FT_PurgeDelegate>("FT_Purge");
                _write = GetDelegate<FT_WriteDelegate>("FT_Write");
                _getStatus = GetDelegate<FT_GetStatusDelegate>("FT_GetStatus");
                _resetDevice = GetDelegate<FT_ResetDeviceDelegate>("FT_ResetDevice");

                uint count = 0;
                if (_createDeviceInfoList(ref count) != 0)
                {
                    SetUnavailable("FTD2XX.dll chargée mais FT_CreateDeviceInfoList a échoué.");
                    return false;
                }

                return true;
            }
            catch (BadImageFormatException)
            {
                SetUnavailable("FTD2XX.dll incompatible (vérifiez l'architecture x64).");
                return false;
            }
            catch (Exception ex)
            {
                SetUnavailable($"FTD2XX.dll chargée mais invalide : {ex.Message}");
                return false;
            }
        }
    }

    private void SetUnavailable(string reason)
    {
        _unavailableReason = reason;
        if (_module != IntPtr.Zero)
        {
            NativeLibrary.Free(_module);
            _module = IntPtr.Zero;
        }
    }

    private T GetDelegate<T>(string exportName) where T : Delegate
    {
        var ptr = NativeLibrary.GetExport(_module, exportName);
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    public int FT_CreateDeviceInfoList(ref uint numDevs)
    {
        if (!TryLoad() || _createDeviceInfoList is null)
            return 2;

        return _createDeviceInfoList(ref numDevs);
    }

    public int FT_GetDeviceInfoDetail(uint index, ref uint flags, ref uint type, ref uint id, ref uint locId,
        byte[] serial, byte[] description, ref IntPtr handle)
    {
        if (!TryLoad() || _getDeviceInfoDetail is null)
            return 2;

        return _getDeviceInfoDetail(index, ref flags, ref type, ref id, ref locId, serial, description, ref handle);
    }

    public int FT_Open(int deviceNumber, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (!TryLoad() || _open is null)
            return 2;

        return _open(deviceNumber, out handle);
    }

    public int FT_Close(IntPtr handle) => Invoke(_close, d => d(handle));
    public int FT_SetBaudRate(IntPtr handle, uint baudRate) => Invoke(_setBaudRate, d => d(handle, baudRate));

    public int FT_SetDataCharacteristics(IntPtr handle, byte wordLength, byte stopBits, byte parity) =>
        Invoke(_setDataCharacteristics, d => d(handle, wordLength, stopBits, parity));

    public int FT_SetFlowControl(IntPtr handle, ushort flowControl, byte xon, byte xoff) =>
        Invoke(_setFlowControl, d => d(handle, flowControl, xon, xoff));

    public int FT_SetLatencyTimer(IntPtr handle, byte latency) =>
        Invoke(_setLatencyTimer, d => d(handle, latency));

    public int FT_SetBreakOn(IntPtr handle) => Invoke(_setBreakOn, d => d(handle));
    public int FT_SetBreakOff(IntPtr handle) => Invoke(_setBreakOff, d => d(handle));
    public int FT_Purge(IntPtr handle, uint mask) => Invoke(_purge, d => d(handle, mask));

    public int FT_Write(IntPtr handle, byte[] buffer, int length, ref uint bytesWritten)
    {
        if (!TryLoad() || _write is null)
            return 2;

        return _write(handle, buffer, length, ref bytesWritten);
    }

    public int FT_GetStatus(IntPtr handle, ref uint rxBytes, ref uint txBytes, ref uint eventStatus)
    {
        if (!TryLoad() || _getStatus is null)
            return 2;

        return _getStatus(handle, ref rxBytes, ref txBytes, ref eventStatus);
    }

    public int FT_ResetDevice(IntPtr handle) => Invoke(_resetDevice, d => d(handle));

    private static int Invoke<T>(T? del, Func<T, int> call) where T : Delegate
    {
        if (del is null)
            return 2;

        return call(del);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_CreateDeviceInfoListDelegate(ref uint numDevs);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int FT_GetDeviceInfoDetailDelegate(
        uint index, ref uint flags, ref uint type, ref uint id, ref uint locId,
        byte[] serial, byte[] description, ref IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_OpenDelegate(int deviceNumber, out IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_CloseDelegate(IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_SetBaudRateDelegate(IntPtr handle, uint baudRate);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_SetDataCharacteristicsDelegate(IntPtr handle, byte wordLength, byte stopBits, byte parity);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_SetFlowControlDelegate(IntPtr handle, ushort flowControl, byte xon, byte xoff);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_SetLatencyTimerDelegate(IntPtr handle, byte latency);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_SetBreakOnDelegate(IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_SetBreakOffDelegate(IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_PurgeDelegate(IntPtr handle, uint mask);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_WriteDelegate(IntPtr handle, byte[] buffer, int length, ref uint bytesWritten);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_GetStatusDelegate(IntPtr handle, ref uint rxBytes, ref uint txBytes, ref uint eventStatus);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FT_ResetDeviceDelegate(IntPtr handle);
}

/// <summary>Façade vers FtdiLibrary — remplace les anciens DllImport statiques.</summary>
internal static class FtdiNative
{
    public const byte Bits8 = 8;
    public const byte StopBits2 = 2;
    public const byte ParityNone = 0;
    public const ushort FlowNone = 0;
    public const byte PurgeRx = 1;
    public const byte PurgeTx = 2;
    public const uint BaudDmx = 250_000;

    private static FtdiLibrary Lib => FtdiLibrary.Instance;

    public static bool IsAvailable() => Lib.IsAvailable;
    public static string? UnavailableReason => Lib.UnavailableReason;

    public static void EnsureProbed() => Lib.EnsureProbed();

    public static int FT_CreateDeviceInfoList(ref uint numDevs) => Lib.FT_CreateDeviceInfoList(ref numDevs);
    public static int FT_GetDeviceInfoDetail(uint index, ref uint flags, ref uint type, ref uint id, ref uint locId,
        byte[] serial, byte[] description, ref IntPtr handle) =>
        Lib.FT_GetDeviceInfoDetail(index, ref flags, ref type, ref id, ref locId, serial, description, ref handle);
    public static int FT_Open(int deviceNumber, out IntPtr handle) => Lib.FT_Open(deviceNumber, out handle);
    public static int FT_Close(IntPtr handle) => Lib.FT_Close(handle);
    public static int FT_SetBaudRate(IntPtr handle, uint baudRate) => Lib.FT_SetBaudRate(handle, baudRate);
    public static int FT_SetDataCharacteristics(IntPtr handle, byte wordLength, byte stopBits, byte parity) =>
        Lib.FT_SetDataCharacteristics(handle, wordLength, stopBits, parity);
    public static int FT_SetFlowControl(IntPtr handle, ushort flowControl, byte xon, byte xoff) =>
        Lib.FT_SetFlowControl(handle, flowControl, xon, xoff);
    public static int FT_SetLatencyTimer(IntPtr handle, byte latency) => Lib.FT_SetLatencyTimer(handle, latency);
    public static int FT_SetBreakOn(IntPtr handle) => Lib.FT_SetBreakOn(handle);
    public static int FT_SetBreakOff(IntPtr handle) => Lib.FT_SetBreakOff(handle);
    public static int FT_Purge(IntPtr handle, uint mask) => Lib.FT_Purge(handle, mask);
    public static int FT_Write(IntPtr handle, byte[] buffer, int length, ref uint bytesWritten) =>
        Lib.FT_Write(handle, buffer, length, ref bytesWritten);
    public static int FT_GetStatus(IntPtr handle, ref uint rxBytes, ref uint txBytes, ref uint eventStatus) =>
        Lib.FT_GetStatus(handle, ref rxBytes, ref txBytes, ref eventStatus);
    public static int FT_ResetDevice(IntPtr handle) => Lib.FT_ResetDevice(handle);

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
