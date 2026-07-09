namespace OpenDMXBridge.Models;

public enum BridgeOperationMode
{
    /// <summary>Art-Net → sortie DMX physique.</summary>
    Bridge,

    /// <summary>Analyse réseau sans sortie DMX.</summary>
    Monitor
}
