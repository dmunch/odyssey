namespace Odyssey;

/// <summary>
/// Represents an event that could not be resolved using the configured type resolver
/// </summary>
public readonly struct UnresolvedEvent
{
    public static readonly UnresolvedEvent Instance = new();
}