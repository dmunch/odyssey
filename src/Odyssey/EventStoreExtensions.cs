using OneOf;

namespace Odyssey;

using AppendResult = OneOf<Success, UnexpectedStreamState>;

public static class EventStoreExtensions
{
    /// <summary>
    /// Copies events from one stream to another within the same event store instance
    /// </summary>
    /// <param name="sourceStore">The event store to copy streams from and to</param>
    /// <param name="sourceStreamId">The source stream identifier</param>
    /// <param name="destinationStreamId">The destination stream identifier</param>
    /// <returns>The append result of the write</returns>
    public static Task<AppendResult> CopyStream(this IEventStore sourceStore, string sourceStreamId, string destinationStreamId)
        => CopyStream(sourceStore, sourceStreamId, sourceStore, destinationStreamId);

    /// <summary>
    /// Copies events from one stream in the source store to another in the destination store
    /// </summary>
    /// <param name="store">The event store to copy streams from and to</param>
    /// <param name="sourceStreamId">The source stream identifier</param>
    /// <param name="destinationStore">The</param>
    /// <param name="destinationStreamId">The destination stream identifier. If not specified the <paramref name="sourceStreamId"/> will be used</param>
    /// <returns>The append result of the write</returns>
    public static async Task<AppendResult> CopyStream(this IEventStore sourceStore, string sourceStreamId, IEventStore destinationStore, string? destinationStreamId = null)
    {
        var eventsToCopy = await sourceStore.ReadStream(sourceStreamId, ReadDirection.Forwards, StreamPosition.Start);
        return await destinationStore.AppendToStream(destinationStreamId ?? sourceStreamId, eventsToCopy.ToList(), StreamState.Any);
    }
}