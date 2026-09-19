using System.IO.Ports;
using OpenDMXBridge.Models;
using OpenDMXBridge.Services.Contracts;
using OpenDMXBridge.Services.Dmx;
using OpenDMXBridge.Services.Ftdi;

namespace OpenDMXBridge.Services.Outputs;

/// <summary>
/// Sortie Enttec Open DMX : D2XX (FTD2XX.dll) ou port série COM (pilote VCP) selon la disponibilité.
/// </summary>
public sealed class OpenDmxOutput : IDmxOutput
{
    private const int DmxSlots = 513;
    private const int TimingLogInterval = 1000;
    private const string D2xxPrefix = "d2xx:";
    private const string ComPrefix = "com:";

    /// <summary>Durée d'émission d'une trame complète : 513 octets × 11 bits à 250 kbauds ≈ 22,6 ms.</summary>
    private const double FrameTransmitMs = DmxSlots * 11 * 1000.0 / Dmx512Timing.DmxBaudRate;

    /// <summary>Marge de sécurité avant le break suivant (latence USB du FTDI).</summary>
    private const double FrameGuardMs = 1.5;

    private readonly ILoggingService _logger;
    private readonly ISettingsService _settings;
    private readonly object _ioLock = new();
    private readonly byte[] _frameBuffer = new byte[DmxSlots];

    private IntPtr _d2xxHandle = IntPtr.Zero;
    private SerialPort? _serialPort;
    private DmxOutputDevice? _connectedDevice;
    private long _framesSent;
    private long _timingSampleCounter;
    private long _lastWriteTimestamp;
    private volatile bool _watchdogRunning;
    private Thread? _watchdogThread;

    public OpenDmxOutput(ILoggingService logger, ISettingsService settings)
    {
        _logger = logger;
        _settings = settings;
        _frameBuffer[0] = 0x00;
    }

    public string OutputType => "OpenDMX";
    public string DisplayName => "Enttec Open DMX (FTDI)";
    public bool SupportsAutoReconnect => true;
    public long FramesSent => Interlocked.Read(ref _framesSent);

    public bool IsDriverAvailable
    {
        get
        {
            if (FtdiUsbDiagnostics.FindFtdiComPorts().Count > 0)
                return true;

            FtdiNative.EnsureProbed();
            return FtdiNative.IsAvailable();
        }
    }

    public string? DriverUnavailableMessage
    {
        get
        {
            if (IsDriverAvailable)
                return null;

            return FtdiNative.UnavailableReason
                   ?? "Aucune interface FTDI (D2XX ou port COM) détectée.";
        }
    }

    public bool IsConnected
    {
        get
        {
            lock (_ioLock)
                return _d2xxHandle != IntPtr.Zero || _serialPort?.IsOpen == true;
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
        var devices = new List<DmxOutputDevice>();

        // D2XX en premier : contrôle direct du break, c'est le mode recommandé pour l'Open DMX.
        FtdiNative.EnsureProbed();
        if (FtdiNative.IsAvailable())
        {
            uint count = 0;
            if (FtdiNative.FT_CreateDeviceInfoList(ref count) == 0)
            {
                for (uint i = 0; i < count; i++)
                {
                    uint flags = 0, type = 0, id = 0, locId = 0;
                    var serial = new byte[16];
                    var description = new byte[64];
                    IntPtr handle = IntPtr.Zero;

                    if (FtdiNative.FT_GetDeviceInfoDetail(i, ref flags, ref type, ref id, ref locId, serial, description, ref handle) != 0)
                        continue;

                    var serialText = TrimNullTerminated(serial);
                    var descText = FormatD2xxDescription(i, type, id, serialText, TrimNullTerminated(description));
                    if ((flags & 0x1) != 0)
                        descText += " — occupé par un autre logiciel";

                    devices.Add(new DmxOutputDevice(
                        $"{D2xxPrefix}{i}",
                        descText,
                        string.IsNullOrWhiteSpace(serialText) ? null : serialText,
                        (int)i));
                }
            }
        }

        // Port série (VCP) en secours.
        foreach (var comPort in FtdiUsbDiagnostics.FindFtdiComPorts())
        {
            devices.Add(new DmxOutputDevice(
                $"{ComPrefix}{comPort}",
                $"Open DMX USB ({comPort} — port série)",
                null,
                -1));
        }

        return devices;
    }

    public Task<bool> ConnectAsync(DmxOutputDevice device, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            DisconnectInternal();

            if (device.Id.StartsWith(ComPrefix, StringComparison.OrdinalIgnoreCase))
                return ConnectSerial(device);

            lock (_ioLock)
            {
                if (device.Id.StartsWith(D2xxPrefix, StringComparison.OrdinalIgnoreCase))
                    return ConnectD2xx(device);

                _logger.Error($"Identifiant de périphérique inconnu : {device.Id}", nameof(OpenDmxOutput));
                return false;
            }
        }, cancellationToken);
    }

    public Task DisconnectAsync() => Task.Run(DisconnectInternal);

    public void SendFrame(ReadOnlySpan<byte> channels)
    {
        lock (_ioLock)
        {
            if (_d2xxHandle == IntPtr.Zero && _serialPort?.IsOpen != true)
                return;

            var copyLength = Math.Min(channels.Length, 512);
            channels.Slice(0, copyLength).CopyTo(_frameBuffer.AsSpan(1));
            if (copyLength < 512)
                _frameBuffer.AsSpan(1 + copyLength, 512 - copyLength).Clear();

            try
            {
                WaitForPreviousFrame();

                if (_serialPort?.IsOpen == true)
                    TransmitDmx512FrameSerial(_serialPort, _frameBuffer);
                else if (_d2xxHandle != IntPtr.Zero)
                    TransmitDmx512FrameD2xx(_d2xxHandle, _frameBuffer);

                Interlocked.Increment(ref _framesSent);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Erreur envoi DMX : {ex.Message}", nameof(OpenDmxOutput));
                var device = _connectedDevice;
                MarkDisconnected();
                if (device is not null)
                    Task.Run(() => TryReconnect(device));
            }
        }
    }

    private bool ConnectSerial(DmxOutputDevice device)
    {
        var portName = device.Id[ComPrefix.Length..];
        if (string.IsNullOrWhiteSpace(portName))
        {
            _logger.Error("Port COM invalide.", nameof(OpenDmxOutput));
            return false;
        }

        FtdiNative.ResetProbe();

        try
        {
            var port = new SerialPort(portName, (int)Dmx512Timing.DmxBaudRate, Parity.None, 8, StopBits.Two)
            {
                WriteTimeout = 2000,
                ReadTimeout = 500,
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false
            };
            port.Open();

            lock (_ioLock)
            {
                _serialPort = port;
                _connectedDevice = device;
                StartWatchdog();
            }

            _logger.Info($"OpenDMX connecté via {portName}", nameof(OpenDmxOutput));
            _logger.Info(
                "Validez les timings break/MAB à l'oscilloscope (Break ≥ 88 µs, MAB ≥ 8 µs, 250 kbaud 8N2).",
                nameof(OpenDmxOutput));
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.Error(FtdiUsbDiagnostics.BuildPortBusyHint(portName), nameof(OpenDmxOutput));
        }
        catch (Exception ex)
        {
            _logger.Error($"Ouverture {portName} échouée : {ex.Message}", nameof(OpenDmxOutput));
        }

        return false;
    }

    private bool ConnectD2xx(DmxOutputDevice device)
    {
        FtdiNative.EnsureProbed();
        if (!FtdiNative.IsAvailable())
        {
            _logger.Error(FtdiNative.UnavailableReason ?? "FTD2XX.dll non disponible.", nameof(OpenDmxOutput));
            return false;
        }

        if (FtdiNative.FT_Open(device.NativeIndex, out _d2xxHandle) != 0 || _d2xxHandle == IntPtr.Zero)
        {
            _logger.Error(FtdiUsbDiagnostics.BuildPortBusyHint(device.Description), nameof(OpenDmxOutput));
            _d2xxHandle = IntPtr.Zero;
            return false;
        }

        try
        {
            ConfigureOpenDmx(_d2xxHandle);
            _connectedDevice = device;
            StartWatchdog();

            _logger.Info($"OpenDMX connecté : {device.Description}", nameof(OpenDmxOutput));
            _logger.Info(
                "Validez les timings break/MAB à l'oscilloscope (Break ≥ 88 µs, MAB ≥ 8 µs, 250 kbaud 8N2).",
                nameof(OpenDmxOutput));
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Configuration D2XX échouée : {ex.Message}", nameof(OpenDmxOutput));
            MarkDisconnected();
            return false;
        }
    }

    private void TransmitDmx512FrameD2xx(IntPtr handle, byte[] buffer)
    {
        var cfg = _settings.Current;
        var breakUs = Math.Max(cfg.BreakMicroseconds, (int)Dmx512Timing.MinBreakMicroseconds);
        var mabUs = Math.Max(cfg.MabMicroseconds, (int)Dmx512Timing.MinMabMicroseconds);
        var logDiagnostics = cfg.EnableTimingDiagnostics;

        WaitForTxQueueEmpty(handle);

        var timing = Dmx512Timing.MeasureBreakAndMab(
            () => Check(FtdiNative.FT_SetBreakOn(handle)),
            () => Check(FtdiNative.FT_SetBreakOff(handle)),
            breakUs,
            mabUs);

        if (logDiagnostics)
            LogTimingSample(timing, breakUs, mabUs);

        uint written = 0;
        Check(FtdiNative.FT_Write(handle, buffer, buffer.Length, ref written));
        _lastWriteTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        if (written != buffer.Length)
            throw new InvalidOperationException("Écriture DMX incomplète.");
    }

    private void TransmitDmx512FrameSerial(SerialPort port, byte[] buffer)
    {
        var cfg = _settings.Current;
        var breakUs = Math.Max(cfg.BreakMicroseconds, (int)Dmx512Timing.MinBreakMicroseconds);
        var mabUs = Math.Max(cfg.MabMicroseconds, (int)Dmx512Timing.MinMabMicroseconds);
        var logDiagnostics = cfg.EnableTimingDiagnostics;

        long breakStart = 0, breakEnd = 0, mabEnd = 0;

        port.BreakState = true;
        if (logDiagnostics)
            breakStart = System.Diagnostics.Stopwatch.GetTimestamp();

        Dmx512Timing.WaitMicroseconds(breakUs);

        port.BreakState = false;
        if (logDiagnostics)
            breakEnd = System.Diagnostics.Stopwatch.GetTimestamp();

        Dmx512Timing.WaitMicroseconds(mabUs);
        if (logDiagnostics)
            mabEnd = System.Diagnostics.Stopwatch.GetTimestamp();

        if (logDiagnostics)
        {
            var breakMeasured = Dmx512Timing.ElapsedMicroseconds(breakStart, breakEnd);
            var mabMeasured = Dmx512Timing.ElapsedMicroseconds(breakEnd, mabEnd);
            LogTimingSample(
                new Dmx512Timing.FrameTimingMeasurement(
                    new Dmx512Timing.PhaseMeasurement(breakMeasured, breakMeasured >= Dmx512Timing.MinBreakMicroseconds, "Break"),
                    new Dmx512Timing.PhaseMeasurement(mabMeasured, mabMeasured >= Dmx512Timing.MinMabMicroseconds, "MAB")),
                breakUs,
                mabUs);
        }

        port.Write(buffer, 0, buffer.Length);
        _lastWriteTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    private void LogTimingSample(Dmx512Timing.FrameTimingMeasurement timing, double requestedBreak, double requestedMab)
    {
        var count = Interlocked.Increment(ref _timingSampleCounter);
        if (count % TimingLogInterval != 0)
            return;

        _logger.Trace(
            $"Timing logiciel — Break: {timing.Break.Microseconds:F1} µs (demandé {requestedBreak}), " +
            $"MAB: {timing.Mab.Microseconds:F1} µs (demandé {requestedMab}). " +
            "Mesure CPU/API uniquement ; valider à l'oscilloscope.",
            nameof(OpenDmxOutput));

        if (!timing.MeetsSpec)
        {
            _logger.Warning(
                $"Timings logiciels sous spec DMX512 — Break: {timing.Break.Microseconds:F1} µs (min {Dmx512Timing.MinBreakMicroseconds}), " +
                $"MAB: {timing.Mab.Microseconds:F1} µs (min {Dmx512Timing.MinMabMicroseconds}). " +
                "Calibrez BreakMicroseconds/MabMicroseconds après mesure matérielle.",
                nameof(OpenDmxOutput));
        }
    }

    /// <summary>
    /// Attend que la trame précédente soit entièrement sortie du boîtier avant d'émettre
    /// le break suivant. Sans cela, à 44 Hz, le break coupe la trame en cours et la sortie clignote.
    /// </summary>
    private void WaitForPreviousFrame()
    {
        var last = _lastWriteTimestamp;
        if (last == 0)
            return;

        var minSpacingMs = FrameTransmitMs + FrameGuardMs;
        while (true)
        {
            var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            var remaining = minSpacingMs - elapsedMs;
            if (remaining <= 0)
                return;

            if (remaining > 2.0)
                Thread.Sleep((int)(remaining - 1.0));
            else
                Thread.SpinWait(50);
        }
    }

    /// <summary>D2XX : attend (au plus 10 ms) que le tampon d'émission du FTDI soit vide.</summary>
    private static void WaitForTxQueueEmpty(IntPtr handle)
    {
        var deadline = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency / 100;
        uint rx = 0, tx = 0, events = 0;
        while (System.Diagnostics.Stopwatch.GetTimestamp() < deadline)
        {
            if (FtdiNative.FT_GetStatus(handle, ref rx, ref tx, ref events) != 0 || tx == 0)
                return;

            Thread.SpinWait(100);
        }
    }
    private static void ConfigureOpenDmx(IntPtr handle)
    {
        Check(FtdiNative.FT_ResetDevice(handle));
        Check(FtdiNative.FT_SetBaudRate(handle, Dmx512Timing.DmxBaudRate));
        Check(FtdiNative.FT_SetDataCharacteristics(handle, FtdiNative.Bits8, FtdiNative.StopBits2, FtdiNative.ParityNone));
        Check(FtdiNative.FT_SetFlowControl(handle, FtdiNative.FlowNone, 0, 0));
        Check(FtdiNative.FT_SetLatencyTimer(handle, 1));
        Check(FtdiNative.FT_Purge(handle, FtdiNative.PurgeRx | FtdiNative.PurgeTx));
    }

    private static string FormatD2xxDescription(uint index, uint type, uint id, string serial, string description)
    {
        if (!string.IsNullOrWhiteSpace(description))
            return description;

        if (!string.IsNullOrWhiteSpace(serial))
            return $"Enttec Open DMX USB ({serial})";

        var productName = (id & 0xFFFF) switch
        {
            0x6001 => "Enttec Open DMX USB",
            _ => type switch
            {
                3 => "FTDI FT245 (Open DMX)",
                _ => "Interface FTDI"
            }
        };

        return $"{productName} (D2XX #{index})";
    }

    private void StartWatchdog()
    {
        if (_watchdogRunning)
            return;

        _watchdogRunning = true;
        _watchdogThread = new Thread(WatchdogLoop)
        {
            Name = "OpenDMX-Watchdog",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal
        };
        _watchdogThread.Start();
    }

    private void StopWatchdog()
    {
        _watchdogRunning = false;
        var thread = _watchdogThread;
        _watchdogThread = null;
        thread?.Join(TimeSpan.FromMilliseconds(500));
    }

    private void WatchdogLoop()
    {
        while (_watchdogRunning)
        {
            Thread.Sleep(500);

            DmxOutputDevice? device = null;
            var shouldReconnect = false;

            lock (_ioLock)
            {
                if (_connectedDevice is null)
                    continue;

                if (_serialPort?.IsOpen == true)
                    continue;

                if (_d2xxHandle == IntPtr.Zero)
                    continue;

                if (!FtdiNative.IsAvailable())
                    continue;

                uint rx = 0, tx = 0, events = 0;
                if (FtdiNative.FT_GetStatus(_d2xxHandle, ref rx, ref tx, ref events) != 0)
                {
                    _logger.Warning("Perte USB FTDI détectée.", nameof(OpenDmxOutput));
                    MarkDisconnected();
                    device = _connectedDevice;
                    shouldReconnect = device is not null;
                }
            }

            if (shouldReconnect && device is not null)
                TryReconnect(device);
        }
    }

    private void MarkDisconnected()
    {
        _lastWriteTimestamp = 0;

        if (_d2xxHandle != IntPtr.Zero)
        {
            if (FtdiNative.IsAvailable())
                FtdiNative.FT_Close(_d2xxHandle);
            _d2xxHandle = IntPtr.Zero;
        }

        if (_serialPort is not null)
        {
            try
            {
                if (_serialPort.IsOpen)
                    _serialPort.Close();
            }
            catch
            {
                // ignore close errors during recovery
            }

            _serialPort.Dispose();
            _serialPort = null;
        }
    }

    private void TryReconnect(DmxOutputDevice device)
    {
        _logger.Info("Tentative de reconnexion…", nameof(OpenDmxOutput));

        lock (_ioLock)
            MarkDisconnected();

        if (device.Id.StartsWith(ComPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (ConnectSerial(device))
                _logger.Info("Reconnexion port série réussie.", nameof(OpenDmxOutput));
            return;
        }

        lock (_ioLock)
        {
            if (!FtdiNative.IsAvailable())
                return;

            if (FtdiNative.FT_Open(device.NativeIndex, out _d2xxHandle) == 0 && _d2xxHandle != IntPtr.Zero)
            {
                try
                {
                    ConfigureOpenDmx(_d2xxHandle);
                    _connectedDevice = device;
                    StartWatchdog();
                    _logger.Info("Reconnexion D2XX réussie — reprise DMX.", nameof(OpenDmxOutput));
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

        lock (_ioLock)
        {
            MarkDisconnected();
            _connectedDevice = null;
        }

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
