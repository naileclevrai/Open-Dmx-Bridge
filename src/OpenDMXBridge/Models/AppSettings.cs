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
}
