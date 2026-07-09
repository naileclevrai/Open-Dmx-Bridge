namespace OpenDMXBridge.Models;

public sealed record LabeledOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
