using OpenDMXBridge.Services.Contracts;

namespace OpenDMXBridge.Models;

/// <summary>
/// Paramètres persistés de l'application.
/// </summary>
public sealed class AppSettings
{
    public string? SelectedNetworkAdapterId { get; set; }
    public byte ArtNetNet { get; set; }
    public byte ArtNetSubNet { get; set; }
    public byte ArtNetUniverse { get; set; }
    public bool AutoStartBridge { get; set; }
    public int DmxRefreshHz { get; set; } = 44;
    public string OutputType { get; set; } = "OpenDMX";
    public string? OutputDeviceId { get; set; }
    public BridgeOperationMode OperationMode { get; set; } = BridgeOperationMode.Bridge;
    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Info;

    public int BreakMicroseconds { get; set; } = 100;

    /// <summary>Durée MAB demandée (µs). À calibrer après mesure oscilloscope. Min DMX512 : 8.</summary>
    public int MabMicroseconds { get; set; } = 12;

    /// <summary>Log TRACE périodique des timings break/MAB mesurés (logiciel).</summary>
    public bool EnableTimingDiagnostics { get; set; }
}
