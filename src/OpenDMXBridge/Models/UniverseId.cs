namespace OpenDMXBridge.Models;

/// <summary>
/// Identifiant unique d'univers DMX/Art-Net pour le dictionnaire multi-univers.
/// </summary>
public readonly struct UniverseId : IEquatable<UniverseId>
{
    public byte Net { get; }
    public byte SubNet { get; }
    public byte Universe { get; }

    public UniverseId(byte net, byte subNet, byte universe)
    {
        Net = net;
        SubNet = subNet;
        Universe = universe;
    }

    public static UniverseId FromArtNet(byte net, byte subNet, byte universe) =>
        new(net, subNet, universe);

    public static UniverseId FromArtNetUniverse(ArtNetUniverse u) =>
        new(u.Net, u.SubNet, u.Universe);

    public bool Equals(UniverseId other) =>
        Net == other.Net && SubNet == other.SubNet && Universe == other.Universe;

    public override bool Equals(object? obj) => obj is UniverseId id && Equals(id);

    public override int GetHashCode() => HashCode.Combine(Net, SubNet, Universe);

    public static bool operator ==(UniverseId left, UniverseId right) => left.Equals(right);
    public static bool operator !=(UniverseId left, UniverseId right) => !left.Equals(right);
}
