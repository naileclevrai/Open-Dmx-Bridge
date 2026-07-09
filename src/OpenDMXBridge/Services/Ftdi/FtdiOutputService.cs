using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Services.Ftdi;

/// <summary>
/// Sortie OpenDMX via FTD2XX. Protocole break + start code + 512 slots.
/// </summary>
public sealed class FtdiOutputService : IFtdiOutputService
{
    private const int DmxSlots = 513;
    private readonly ILoggingService _logger;
    private readonly object _ioLock = new();
    private readonly byte[] _frameBuffer = new byte[DmxSlots];

    private IntPtr _handle = IntPtr.Zero;
    private int _deviceIndex = -1;
    private string? _deviceDescription;
    private long _packetsSent;

    public FtdiOutputService(ILoggingService logger)
    {
        _logger = logger;
        _frameBuffer[0] = 0x00;
    }

    public bool IsConnected
    {
        get
        {
            lock (_ioLock)
                return _handle != IntPtr.Zero;
        }
    }

    public string? DeviceDescription
    {
        get
        {
            lock (_ioLock)
                return _deviceDescription;
        }
    }

    public long PacketsSent => Interlocked.Read(ref _packetsSent);

    public IReadOnlyList<FtdiDeviceInfo> EnumerateDevices()
    {
        if (!FtdiNative.IsAvailable())
        {
            _logger.Warning("FTD2XX.dll introuvable ou architecture incompatible.", nameof(FtdiOutputService));
            return Array.Empty<FtdiDeviceInfo>();
        }

        uint count = 0;
        if (FtdiNative.FT_CreateDeviceInfoList(ref count) != 0 || count == 0)
            return Array.Empty<FtdiDeviceInfo>();

        var devices = new List<FtdiDeviceInfo>((int)count);
        for (uint i = 0; i < count; i++)
        {
            uint flags = 0, type = 0, id = 0, locId = 0;
            var serial = new byte[16];
            var description = new byte[64];
            IntPtr handle = IntPtr.Zero;

            if (FtdiNative.FT_GetDeviceInfoDetail(i, ref flags, ref type, ref id, ref locId, serial, description, ref handle) != 0)
                continue;

            devices.Add(new FtdiDeviceInfo(
                (int)i,
                TrimNullTerminated(description),
                TrimNullTerminated(serial),
                false));
        }

        return devices;
    }

    public Task<bool> ConnectAsync(int deviceIndex, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_ioLock)
            {
                DisconnectInternal();

                if (!FtdiNative.IsAvailable())
                {
                    _logger.Error("FTD2XX.dll non disponible.", nameof(FtdiOutputService));
                    return false;
                }

                if (FtdiNative.FT_Open(deviceIndex, out _handle) != 0 || _handle == IntPtr.Zero)
                {
                    _logger.Error($"Ouverture FTDI index {deviceIndex} échouée.", nameof(FtdiOutputService));
                    _handle = IntPtr.Zero;
                    return false;
                }

                ConfigureOpenDmx(_handle);
                _deviceIndex = deviceIndex;
                _deviceDescription = EnumerateDevices().FirstOrDefault(d => d.Index == deviceIndex)?.Description
                                     ?? $"FTDI #{deviceIndex}";

                _logger.Info($"OpenDMX connecté : {_deviceDescription}", nameof(FtdiOutputService));
                return true;
            }
        }, cancellationToken);
    }

    public Task DisconnectAsync()
    {
        return Task.Run(() =>
        {
            lock (_ioLock)
                DisconnectInternal();
        });
    }

    public void SendFrame(ReadOnlySpan<byte> dmxData)
    {
        lock (_ioLock)
        {
            if (_handle == IntPtr.Zero)
                return;

            var copyLength = Math.Min(dmxData.Length, 512);
            dmxData.Slice(0, copyLength).CopyTo(_frameBuffer.AsSpan(1));
            if (copyLength < 512)
                _frameBuffer.AsSpan(1 + copyLength, 512 - copyLength).Clear();

            try
            {
                TransmitOpenDmxFrame(_handle, _frameBuffer);
                Interlocked.Increment(ref _packetsSent);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Erreur envoi DMX : {ex.Message}", nameof(FtdiOutputService));
                TryReconnectInternal();
            }
        }
    }

    private void TransmitOpenDmxFrame(IntPtr handle, byte[] buffer)
    {
        FtdiNative.FT_SetBreakOn(handle);
        Thread.SpinWait(2000);
        FtdiNative.FT_SetBreakOff(handle);
        Thread.SpinWait(200);

        uint written = 0;
        var status = FtdiNative.FT_Write(handle, buffer, buffer.Length, ref written);
        if (status != 0)
            throw new InvalidOperationException(FtdiNative.GetStatusMessage(status));

        FtdiNative.FT_Purge(handle, FtdiNative.PurgeTx);
    }

    private static void ConfigureOpenDmx(IntPtr handle)
    {
        Check(FtdiNative.FT_ResetDevice(handle));
        Check(FtdiNative.FT_SetBaudRate(handle, FtdiNative.BaudDmx));
        Check(FtdiNative.FT_SetDataCharacteristics(handle, FtdiNative.Bits8, FtdiNative.StopBits2, FtdiNative.ParityNone));
        Check(FtdiNative.FT_SetFlowControl(handle, FtdiNative.FlowNone, 0, 0));
        Check(FtdiNative.FT_SetLatencyTimer(handle, 2));
        Check(FtdiNative.FT_Purge(handle, FtdiNative.PurgeRx | FtdiNative.PurgeTx));
    }

    private void TryReconnectInternal()
    {
        if (_deviceIndex < 0)
            return;

        _logger.Info("Tentative de reconnexion FTDI…", nameof(FtdiOutputService));
        DisconnectInternal();

        if (FtdiNative.FT_Open(_deviceIndex, out _handle) == 0 && _handle != IntPtr.Zero)
        {
            try
            {
                ConfigureOpenDmx(_handle);
                _logger.Info("Reconnexion FTDI réussie.", nameof(FtdiOutputService));
            }
            catch
            {
                DisconnectInternal();
            }
        }
    }

    private void DisconnectInternal()
    {
        if (_handle != IntPtr.Zero)
        {
            FtdiNative.FT_Close(_handle);
            _handle = IntPtr.Zero;
            _logger.Info("Interface FTDI fermée.", nameof(FtdiOutputService));
        }
    }

    private static void Check(int status)
    {
        if (status != 0)
            throw new InvalidOperationException(FtdiNative.GetStatusMessage(status));
    }

    private static string TrimNullTerminated(byte[] bytes)
    {
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0)
            length = bytes.Length;

        return System.Text.Encoding.ASCII.GetString(bytes, 0, length).Trim();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}
