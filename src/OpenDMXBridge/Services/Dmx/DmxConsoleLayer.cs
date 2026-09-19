namespace OpenDMXBridge.Services.Dmx;

/// <summary>Règle de fusion entre la console locale et le flux Art-Net.</summary>
public enum ConsoleMergeMode
{
    /// <summary>Highest Takes Precedence : la valeur la plus haute gagne.</summary>
    Htp,

    /// <summary>La console écrase la valeur Art-Net sur les canaux qu'elle tient.</summary>
    Override
}

/// <summary>
/// Micro console DMX : couche de valeurs locales fusionnée dans la trame sortante.
/// Un canal n'agit que s'il est « tenu » (Set) ; Release le rend au flux Art-Net.
/// Thread-safe : l'UI écrit, la boucle DMX lit à 44 Hz.
/// </summary>
public sealed class DmxConsoleLayer
{
    public const int SlotCount = UniverseBuffer.SlotCount;

    private readonly object _sync = new();
    private readonly byte[] _levels = new byte[SlotCount];
    private readonly bool[] _held = new bool[SlotCount];
    private int _heldCount;

    private volatile bool _enabled;
    private volatile bool _blackout;
    private volatile int _master = 255;
    private volatile int _mode = (int)ConsoleMergeMode.Htp;

    /// <summary>Quand false, la couche est ignorée (aucune influence sur la sortie).</summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>Blackout : force toute la trame à zéro (Art-Net compris) tant que la console est active.</summary>
    public bool Blackout
    {
        get => _blackout;
        set => _blackout = value;
    }

    /// <summary>Grand master 0–255 appliqué aux canaux tenus par la console.</summary>
    public int Master
    {
        get => _master;
        set => _master = Math.Clamp(value, 0, 255);
    }

    public ConsoleMergeMode Mode
    {
        get => (ConsoleMergeMode)_mode;
        set => _mode = (int)value;
    }

    public int HeldCount => Volatile.Read(ref _heldCount);

    public void Set(int channel, byte level)
    {
        if (!TryIndex(channel, out var i))
            return;

        lock (_sync)
        {
            if (!_held[i])
            {
                _held[i] = true;
                _heldCount++;
            }

            _levels[i] = level;
        }
    }

    public void Release(int channel)
    {
        if (!TryIndex(channel, out var i))
            return;

        lock (_sync)
        {
            if (_held[i])
            {
                _held[i] = false;
                _heldCount--;
            }

            _levels[i] = 0;
        }
    }

    public void ReleaseAll()
    {
        lock (_sync)
        {
            Array.Clear(_held);
            Array.Clear(_levels);
            _heldCount = 0;
        }
    }

    public bool IsHeld(int channel)
    {
        if (!TryIndex(channel, out var i))
            return false;

        lock (_sync)
            return _held[i];
    }

    public byte Get(int channel)
    {
        if (!TryIndex(channel, out var i))
            return 0;

        lock (_sync)
            return _levels[i];
    }

    /// <summary>Fusionne la console dans une trame de 512 canaux (index 0 = canal 1).</summary>
    public void Merge(Span<byte> frame)
    {
        if (!_enabled)
            return;

        if (_blackout)
        {
            frame.Clear();
            return;
        }

        lock (_sync)
        {
            if (_heldCount == 0)
                return;

            var master = _master;
            var overrideMode = _mode == (int)ConsoleMergeMode.Override;
            var count = Math.Min(frame.Length, SlotCount);

            for (var i = 0; i < count; i++)
            {
                if (!_held[i])
                    continue;

                var value = (byte)(_levels[i] * master / 255);
                if (overrideMode || value > frame[i])
                    frame[i] = value;
            }
        }
    }

    private static bool TryIndex(int channel, out int index)
    {
        index = channel - 1;
        return index >= 0 && index < SlotCount;
    }
}
