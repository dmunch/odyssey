namespace Odyssey;

/// <summary>
/// Interface for cloneable event stores (those that can be copied)
/// </summary>
public interface ICloneable
{
    /// <summary>
    /// Copies all of the event streams from the event store instance to the target
    /// </summary>
    /// <param name="target">The target event store to write to</param>
    /// <returns></returns>
    Task CopyTo(IEventStore target, CancellationToken cancellationToken = default);
}