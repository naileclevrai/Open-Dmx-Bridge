namespace OpenDMXBridge.Services.Dmx;

/// <summary>
/// Double buffer 512 slots avec échange atomique (évite les trames incomplètes).
/// Le thread réseau écrit dans le buffer back puis publie via Interlocked.Exchange.
/// </summary>
public sealed class UniverseBuffer
{
    public const int SlotCount = 512;

    private readonly byte[][] _buffers = { new byte[SlotCount], new byte[SlotCount] };
    private int _readIndex;

    /// <summary>
    /// Fusionne un patch DMX dans le buffer publié (copie front→back, patch, exchange).
    /// Aucune allocation — thread-safe pour un seul producteur réseau.
    /// </summary>
    public void ApplyPatch(ReadOnlySpan<byte> data, int startChannel = 1)
    {
        if (data.Length == 0)
            return;

        var readIndex = Volatile.Read(ref _readIndex);
        var writeIndex = 1 - readIndex;

        Buffer.BlockCopy(_buffers[readIndex], 0, _buffers[writeIndex], 0, SlotCount);

        var start = Math.Clamp(startChannel - 1, 0, SlotCount - 1);
        var copyLen = Math.Min(data.Length, SlotCount - start);
        data.Slice(0, copyLen).CopyTo(_buffers[writeIndex].AsSpan(start));

        Interlocked.Exchange(ref _readIndex, writeIndex);
    }

    /// <summary>Copie atomique du buffer publié vers la destination (512 octets).</summary>
    public void CopySnapshot(Span<byte> destination)
    {
        if (destination.Length < SlotCount)
            throw new ArgumentException("Le buffer destination doit faire au moins 512 octets.", nameof(destination));

        var idx = Volatile.Read(ref _readIndex);
        _buffers[idx].AsSpan().CopyTo(destination);
    }
}
