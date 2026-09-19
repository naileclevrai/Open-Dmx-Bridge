namespace OpenDMXBridge.Services.Contracts;

/// <summary>
/// État du pilote FTDI D2XX (chargement dynamique, sans crash au démarrage).
/// </summary>
public interface IFtdiDriverStatus
{
    bool IsAvailable { get; }
    string? UnavailableMessage { get; }
    string? GetDetectionHint();
    void Probe();
    void ResetProbe();
}
