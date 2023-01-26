namespace Odyssey;

/// <summary>
/// Delegate for handling the case where a previously stored event's CLR type cannot be resolved
/// </summary>
/// <param name="cosmosEvent">The stored event</param>
/// <returns>A fallback event otherwise an exception will be thrown</returns>
public delegate EventData UnresolvedTypeStrategy(CosmosEvent cosmosEvent);

public static class UnresolvedTypeStrategies
{
    /// <summary>
    /// A strategy that throws if the event type cannot be resolved
    /// </summary>
    /// <returns></returns>
    public static readonly UnresolvedTypeStrategy Throw = (CosmosEvent cosmosEvent)
        => throw new ArgumentException($"The CLR type for event {cosmosEvent.EventType} cannot be resolved");

    /// <summary>
    /// A strategy that ignores the unresolved type and falls back to the <see cref="UnresolvedEvent"/> type
    /// </summary>
    /// <returns>An instance of <see cref="EventData"/> that does not contain any event data</returns>
    public static readonly UnresolvedTypeStrategy Skip = (CosmosEvent cosmosEvent)
        => new EventData(
            cosmosEvent.EventId,
            cosmosEvent.EventType,
            UnresolvedEvent.Instance,
            cosmosEvent.Metadata
        )
        {
            EventNumber = cosmosEvent.EventNumber
        };
}