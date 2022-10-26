namespace Odyssey;

public interface IEventStore
{
    Task AppendToStream(string streamId, IReadOnlyList<EventData> events, StreamState expectedState, CancellationToken cancellationToken);
    Task AppendToStream(string streamId, IReadOnlyList<EventData> events, StreamRevision expectedRevision, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<EventData>> ReadStream(string streamId, Direction direction, StreamPosition position, CancellationToken cancellationToken);
}