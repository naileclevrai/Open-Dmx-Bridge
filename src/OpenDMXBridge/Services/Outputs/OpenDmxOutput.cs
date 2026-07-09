using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;
using OpenDMXBridge.Services.Dmx;
using OpenDMXBridge.Services.Ftdi;

namespace OpenDMXBridge.Services.Outputs;

/// <summary>
/// Sortie OpenDMX via FTD2XX avec timings DMX512 (break / MAB / start code / 512 slots)
/// et reconnexion USB automatique.
/// </summary>
public sealed class OpenDmxOutput : IDmxOutput
{
    private const int DmxSlots = 513;
    private const double BreakMicroseconds = 100;
    private const double MabMicroseconds = 12;

    private readonly ILoggingService _logger;
    private readonly object _ioLock = new();
    private readonly byte[] _frameBuffer = new byte[DmxSlots];

    private IntPtr _handle = IntPtr.Zero;
    private DmxOutputDevice? _connectedDevice;
    private long _framesSent;
    private volatile bool _watchdogRunning;
    private Thread? _watchdogThread;

    public OpenDmxOutput(ILoggingService logger)
    {
        _logger = logger;
        _frameBuffer[0] = 0x00;
    }

    public string OutputType => "OpenDMX";
    public string DisplayName => "Enttec Open DMX (FTDI)";
    public bool SupportsAutoReconnect => true;
    public long FramesSent => Interlocked.Read(ref _framesSent);

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
                return _connectedDevice?.Description;
        }
    }

    public IReadOnlyList<DmxOutputDevice> EnumerateDevices()
    {
        if (!FtdiNative.IsAvailable())
        {
            _logger.Warning("FTD2XX.dll introuvable ou architecture incompatible.", nameof(OpenDmxOutput));
            return Array.Empty<DmxOutputDevice>();
        }

        uint count = 0;
        if (FtdiNative.FT_CreateDeviceInfoList(ref count) != 0 || count == 0)
            return Array.Empty<DmxOutputDevice>();

        var devices = new DmxOutputDevice[count];
        var index = 0;
        for (uint i = 0; i < count; i++)
        {
            uint flags = 0, type = 0, id = 0, locId = 0;
            var serial = new byte[16];
            var description = new byte[64];
            IntPtr handle = IntPtr.Zero;

            if (FtdiNative.FT_GetDeviceInfoDetail(i, ref flags, ref type, ref id, ref locId, serial, description, ref handle) != 0)
                continue;

            devices[index++] = new DmxOutputDevice(
                $"ftdi:{i}",
                TrimNullTerminated(description),
                TrimNullTerminated(serial),
                (int)i);
        }

        if (index == devices.Length)
            return devices;

        var trimmed = new DmxOutputDevice[index];
        Array.Copy(devices, trimmed, index);
        return trimmed;
    }

    public Task<bool> ConnectAsync(DmxOutputDevice device, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            lock (_ioLock)
            {
                DisconnectInternal();

                if (!FtdiNative.IsAvailable())
                {
                    _logger.Error("FTD2XX.dll non disponible.", nameof(OpenDmxOutput));
                    return false;
                }

                if (FtdiNative.FT_Open(device.NativeIndex, out _handle) != 0 || _handle == IntPtr.Zero)
                {
                    _logger.Error($"Ouverture FTDI index {device.NativeIndex} échouée.", nameof(OpenDmxOutput));
                    _handle = IntPtr.Zero;
                    return false;
                }

                ConfigureOpenDmx(_handle);
                _connectedDevice = device;
                StartWatchdog();

                _logger.Info($"OpenDMX connecté : {device.Description}", nameof(OpenDmxOutput));
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

    public void SendFrame(ReadOnlySpan<byte> channels)
    {
        lock (_ioLock)
        {
            if (_handle == IntPtr.Zero)
                return;

            var copyLength = Math.Min(channels.Length, 512);
            channels.Slice(0, copyLength).CopyTo(_frameBuffer.AsSpan(1));
            if (copyLength < 512)
                _frameBuffer.AsSpan(1 + copyLength, 512 - copyLength).Clear();

            try
            {
                TransmitDmx512Frame(_handle, _frameBuffer);
                Interlocked.Increment(ref _framesSent);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Erreur envoi DMX : {ex.Message}", nameof(OpenDmxOutput));
                var device = _connectedDevice;
                MarkDisconnected();
                if (device is not null)
                    TryReconnect(device);
            }
        }
    }

    private void TransmitDmx512Frame(IntPtr handle, byte[] buffer)
    {
        Check(FtdiNative.FT_SetBaudRate(handle, FtdiNative.BaudDmx));

        Check(FtdiNative.FT_SetBreakOn(handle));
        HighResolutionWait.Microseconds(BreakMicroseconds);

        Check(FtdiNative.FT_SetBreakOff(handle));
        HighResolutionWait.Microseconds(MabMicroseconds);

        uint written = 0;
        Check(FtdiNative.FT_Write(handle, buffer, buffer.Length, ref written));
        if (written != buffer.Length)
            throw new InvalidOperationException("Écriture DMX incomplète.");

        Check(FtdiNative.FT_Purge(handle, FtdiNative.PurgeTx));
    }

    private static void ConfigureOpenDmx(IntPtr handle)
    {
        Check(FtdiNative.FT_ResetDevice(handle));
        Check(FtdiNative.FT_SetBaudRate(handle, FtdiNative.BaudDmx));
        Check(FtdiNative.FT_SetDataCharacteristics(handle, FtdiNative.Bits8, FtdiNative.StopBits2, FtdiNative.ParityNone));
        Check(FtdiNative.FT_SetFlowControl(handle, FtdiNative.FlowNone, 0, 0));
        Check(FtdiNative.FT_SetLatencyTimer(handle, 1));
        Check(FtdiNative.FT_Purge(handle, FtdiNative.PurgeRx | FtdiNative.PurgeTx));
    }

    private void StartWatchdog()
    {
        if (_watchdogRunning)
            return;

        _watchdogRunning = true;
        _watchdogThread = new Thread(WatchdogLoop)
        {
            Name = "FTDI-Watchdog",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };
        _watchdogThread.Start();
    }

    private void StopWatchdog()
    {
        _watchdogRunning = false;
        _watchdogThread?.Join(TimeSpan.FromSeconds(2));
        _watchdogThread = null;
    }

    private void WatchdogLoop()
    {
        while (_watchdogRunning)
        {
            Thread.Sleep(500);

            DmxOutputDevice? device;
            lock (_ioLock)
            {
                if (_handle == IntPtr.Zero || _connectedDevice is null)
                    continue;

                uint rx = 0, tx = 0, events = 0;
                if (FtdiNative.FT_GetStatus(_handle, ref rx, ref tx, ref events) != 0)
                {
                    _logger.Warning("Perte USB FTDI détectée.", nameof(OpenDmxOutput));
                    MarkDisconnected();
                    device = _connectedDevice;
                }
                else
                {
                    continue;
                }
            }

            if (device is not null)
                TryReconnect(device);
        }
    }

    private void MarkDisconnected()
    {
        if (_handle != IntPtr.Zero)
        {
            FtdiNative.FT_Close(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private void TryReconnect(DmxOutputDevice device)
    {
        _logger.Info("Tentative de reconnexion USB…", nameof(OpenDmxOutput));

        lock (_ioLock)
        {
            MarkDisconnected();

            if (FtdiNative.FT_Open(device.NativeIndex, out _handle) == 0 && _handle != IntPtr.Zero)
            {
                try
                {
                    ConfigureOpenDmx(_handle);
                    _connectedDevice = device;
                    _logger.Info("Reconnexion USB réussie — reprise DMX.", nameof(OpenDmxOutput));
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Reconnexion échouée : {ex.Message}", nameof(OpenDmxOutput));
                    MarkDisconnected();
                }
            }
        }
    }

    private void DisconnectInternal()
    {
        StopWatchdog();
        MarkDisconnected();
        _connectedDevice = null;
        _logger.Info("Interface OpenDMX fermée.", nameof(OpenDmxOutput));
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
