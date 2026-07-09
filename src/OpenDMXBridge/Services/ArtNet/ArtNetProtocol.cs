namespace OpenDMXBridge.Services.ArtNet;

/// <summary>
/// Constantes et validation des paquets Art-Net (ArtDMX 0x5000).
/// </summary>
internal static class ArtNetProtocol
{
    public const int Port = 6454;
    public const int MinPacketSize = 18;
    public const int MaxDmxSlots = 512;

    private static ReadOnlySpan<byte> ArtNetId => "Art-Net\0"u8;

    public const ushort OpCodeArtDmx = 0x5000;

    public static bool TryParseArtDmx(
        ReadOnlySpan<byte> buffer,
        out byte net,
        out byte subnet,
        out byte universe,
        out ReadOnlySpan<byte> dmxData,
        out int dataLength)
    {
        net = subnet = universe = 0;
        dmxData = ReadOnlySpan<byte>.Empty;
        dataLength = 0;

        if (buffer.Length < MinPacketSize)
            return false;

        if (!buffer.Slice(0, 8).SequenceEqual(ArtNetId))
            return false;

        var opCode = (ushort)(buffer[8] | (buffer[9] << 8));
        if (opCode != OpCodeArtDmx)
            return false;

        var protVer = (ushort)(buffer[10] | (buffer[11] << 8));
        if (protVer < 14)
            return false;

        universe = (byte)(buffer[14] & 0x0F);
        subnet = (byte)((buffer[14] >> 4) & 0x0F);
        net = (byte)(buffer[15] & 0x7F);

        dataLength = (buffer[16] << 8) | buffer[17];
        if (dataLength < 0 || dataLength > MaxDmxSlots)
            return false;

        var dataOffset = 18;
        if (buffer.Length < dataOffset + dataLength)
            return false;

        dmxData = buffer.Slice(dataOffset, dataLength);
        return true;
    }
}
