namespace Odyssey.EventConsumer;

internal sealed class TypeMap
{
    public Dictionary<string, Type> Value { get; }

    public TypeMap(Dictionary<string, Type> value)
    {
        Value = value;
    }
}
