namespace OpenDMXBridge.Models;

/// <summary>
/// Identifiant d'univers Art-Net (Net / SubNet / Universe).
/// </summary>
public readonly record struct ArtNetUniverse(byte Net, byte SubNet, byte Universe)
{
    public ushort PortAddress => (ushort)((Net << 8) | (SubNet << 4) | (Universe & 0x0F));

    public static ArtNetUniverse FromPortAddress(ushort portAddress) =>
        new(
            (byte)((portAddress >> 8) & 0x7F),
            (byte)((portAddress >> 4) & 0x0F),
            (byte)(portAddress & 0x0F));

    public static ArtNetUniverse FromPacketBytes(byte portLo, byte portHi) =>
        new((byte)(portHi & 0x7F), (byte)((portLo >> 4) & 0x0F), (byte)(portLo & 0x0F));

    public override string ToString() => $"{Net}.{SubNet}.{Universe}";
}
