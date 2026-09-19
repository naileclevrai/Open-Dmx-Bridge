using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;

const string DllName = "FTD2XX.dll";

[DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
static extern int FT_CreateDeviceInfoList(ref uint numDevs);

[DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
static extern int FT_GetDeviceInfoDetail(
    uint index, ref uint flags, ref uint type, ref uint id, ref uint locId,
    byte[] serial, byte[] description, ref IntPtr handle);

[DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
static extern int FT_Open(int deviceNumber, out IntPtr handle);

[DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
static extern int FT_OpenEx(IntPtr arg, uint flags, out IntPtr handle);

[DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
static extern int FT_Close(IntPtr handle);

[DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
static extern int FT_ListDevices(out uint numDevs, IntPtr buffer, uint flags);

const uint FT_OPEN_BY_LOCATION = 4;
const uint FT_LIST_NUMBER_ONLY = 0x80000000;

uint count = 0;
var status = FT_CreateDeviceInfoList(ref count);
Console.WriteLine($"FT_CreateDeviceInfoList => status={status}, count={count}");

var listStatus = FT_ListDevices(out var listCount, IntPtr.Zero, FT_LIST_NUMBER_ONLY);
Console.WriteLine($"FT_ListDevices(NUMBER_ONLY) => status={listStatus}, count={listCount}");

for (uint i = 0; i < count; i++)
{
    uint flags = 0, type = 0, id = 0, locId = 0;
    var serial = new byte[16];
    var description = new byte[64];
    IntPtr unusedHandle = IntPtr.Zero;

    var detailStatus = FT_GetDeviceInfoDetail(i, ref flags, ref type, ref id, ref locId, serial, description, ref unusedHandle);
    Console.WriteLine($"  [{i}] detail={detailStatus} flags=0x{flags:X} type={type} id=0x{id:X8} locId=0x{locId:X}");
    Console.WriteLine($"       serial='{Trim(serial)}' description='{Trim(description)}'");

    var openStatus = FT_Open((int)i, out var h);
    Console.WriteLine($"       FT_Open(index) => {openStatus}, handle=0x{h.ToInt64():X}");
    if (openStatus == 0 && h != IntPtr.Zero)
        FT_Close(h);

    if (locId != 0)
    {
        var locPtr = new IntPtr(unchecked((int)locId));
        var openExStatus = FT_OpenEx(locPtr, FT_OPEN_BY_LOCATION, out var h2);
        Console.WriteLine($"       FT_OpenEx(locId) => {openExStatus}, handle=0x{h2.ToInt64():X}");
        if (openExStatus == 0 && h2 != IntPtr.Zero)
            FT_Close(h2);
    }
}

Console.WriteLine();
Console.WriteLine("=== Test COM6 (VCP) ===");
try
{
    using var port = new SerialPort("COM6", 250000, Parity.None, 8, StopBits.Two)
    {
        WriteTimeout = 1000,
        ReadTimeout = 1000
    };
    port.Open();
    Console.WriteLine($"COM6 ouvert OK");
    port.BreakState = true;
    Thread.Sleep(1);
    port.BreakState = false;
    var frame = new byte[513];
    frame[1] = 255;
    port.Write(frame, 0, frame.Length);
    Console.WriteLine("Trame DMX test envoyée via COM6");
    port.Close();
}
catch (Exception ex)
{
    Console.WriteLine($"COM6 échec: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("=== Test D2XX FT_Open(0) ===");
var openOnly = FT_Open(0, out var onlyHandle);
Console.WriteLine($"FT_Open => {openOnly}, handle=0x{onlyHandle.ToInt64():X}");
if (openOnly == 0 && onlyHandle != IntPtr.Zero)
    FT_Close(onlyHandle);

static string Trim(byte[] bytes)
{
    var len = Array.IndexOf(bytes, (byte)0);
    if (len < 0) len = bytes.Length;
    return Encoding.ASCII.GetString(bytes, 0, len);
}
