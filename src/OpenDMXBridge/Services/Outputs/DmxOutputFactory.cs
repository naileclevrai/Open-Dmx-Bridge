using OpenDMXBridge.Services.Contracts;
using OpenDMXBridge.Services.Outputs;

namespace OpenDMXBridge.Services.Outputs;

public sealed class DmxOutputFactory : IDmxOutputFactory
{
    private readonly Dictionary<string, IDmxOutput> _outputs;

    public DmxOutputFactory(
        OpenDmxOutput openDmx,
        NullDmxOutput nullOutput,
        EnttecProOutput enttecPro,
        DmxKingOutput dmxKing,
        SacnOutput sacn,
        ArtNetOutput artNet)
    {
        _outputs = new Dictionary<string, IDmxOutput>(StringComparer.OrdinalIgnoreCase)
        {
            [openDmx.OutputType] = openDmx,
            [nullOutput.OutputType] = nullOutput,
            [enttecPro.OutputType] = enttecPro,
            [dmxKing.OutputType] = dmxKing,
            [sacn.OutputType] = sacn,
            [artNet.OutputType] = artNet
        };
    }

    public IReadOnlyList<string> AvailableOutputTypes => _outputs.Keys.OrderBy(k => k).ToArray();

    public IDmxOutput Create(string outputType)
    {
        if (_outputs.TryGetValue(outputType, out var output))
            return output;

        throw new ArgumentException($"Type de sortie inconnu : {outputType}", nameof(outputType));
    }
}
