namespace Odyssey;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using O9d.Guard;
using OneOf;
using OneOf.Types;
using AppendResult = OneOf.OneOf<Success, UnexpectedStreamState>;

public sealed class InMemoryEventStore : IEventStore, ICloneable
{
    private static readonly IReadOnlyCollection<EventData> EmptyStream = Array.Empty<EventData>();
    private ConcurrentDictionary<string, List<EventData>> _streams = new();

    // We could probably use dictionaries rather than InMemoryEventStore but this avoids
    // having to a deep clone of the streams
    private readonly ConcurrentDictionary<string, InMemoryEventStore> _snapshots = new();
    private SemaphoreSlim _snapshotLock = new SemaphoreSlim(1);

    public Task Initialize(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<AppendResult> AppendToStream(string streamId, IReadOnlyList<EventData> events, StreamState expectedState, CancellationToken cancellationToken = default)
    {
        streamId.NotNullOrWhiteSpace();
        events.NotNull();

        bool exists = _streams.TryGetValue(streamId, out List<EventData>? stream);

        static Task<AppendResult> Failed(StreamState state) => Task.FromResult<AppendResult>(new UnexpectedStreamState(state));

        switch (expectedState)
        {
            case { } when expectedState == StreamState.NoStream:
                if (exists)
                {
                    return Failed(expectedState);
                }
                break;
            case { } when expectedState == StreamState.StreamExists:
                if (!exists)
                {
                    return Failed(expectedState);
                }
                break;
            case { } when expectedState >= 0:
                if ((stream!.Count - 1) != expectedState)
                {
                    return Failed(expectedState);
                }
                break;
        }

        if (!exists)
        {
            stream = new();
            _streams.TryAdd(streamId, stream);
        }

        stream.NotNull();

        long currentVersion = stream.Count - 1;
        foreach (var @event in events)
        {
            @event.EventNumber = ++currentVersion;
            stream.Add(@event);
        }

        return Task.FromResult<AppendResult>(Success.Instance);
    }

    public Task<IReadOnlyCollection<EventData>> ReadStream(string streamId, ReadDirection direction, StreamPosition position, CancellationToken cancellationToken = default)
    {
        streamId.NotNullOrWhiteSpace();

        if (!_streams.ContainsKey(streamId))
        {
            return Task.FromResult(EmptyStream);
        }

        var events = _streams[streamId];

        if (direction == ReadDirection.Backwards)
        {
            var reversed = new List<EventData>(events.Count);
            for (int i = events.Count - 1; i >= 0; i--)
            {
                reversed.Add(events[i]);
            }

            return Task.FromResult<IReadOnlyCollection<EventData>>(reversed.AsReadOnly());
        }

        return Task.FromResult<IReadOnlyCollection<EventData>>(events.AsReadOnly());
    }

    public Task<OneOf<EventData, NotFound>> ReadStreamEvent(string streamId, long eventNumber, CancellationToken cancellationToken = default)
    {
        streamId.NotNullOrWhiteSpace();
        if (eventNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eventNumber), "Event number cannot be negative");
        }

        if (!_streams.TryGetValue(streamId, out var stream) || eventNumber >= stream.Count)
        {
            return Task.FromResult<OneOf<EventData, NotFound>>(new NotFound());
        }

        return Task.FromResult<OneOf<EventData, NotFound>>(stream[(int)eventNumber]);
    }

    /// <summary>
    /// Deletes the stream with the specified stream identifier
    /// </summary>
    /// <param name="streamId"></param>
    /// <returns></returns>
    public bool DeleteStream(string streamId)
    {
        return _streams.TryRemove(streamId, out _);
    }

    /// <summary>
    /// Copies in-memory streams to the target event store
    /// </summary>
    /// <param name="target">The target event store to write to</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task CopyTo(IEventStore target, CancellationToken cancellationToken = default)
    {
        target.NotNull();

        foreach ((string streamId, List<EventData> events) in _streams)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                await target.AppendToStream(streamId, events, StreamState.NoStream, cancellationToken);
            }
        };
    }

    /// <summary>
    /// Resets the entire in-memory store, clearing all streams
    /// </summary>
    public void Reset()
    {
        _streams.Clear();
    }

    /// <summary>
    /// Creates a snapshot of all streams in the event store
    /// </summary>
    /// <returns>The snapshot identifier</returns>
    public async Task<string> CreateSnapshot(CancellationToken cancellationToken = default)
    {
        try
        {
            // This only adds thread safety around the action of creating actions
            // It's still possible that writes are happening whilst a snapshot is occurring
            // Probably okay for the in-memory case but perhaps a good reason to move this out to a snapshottable wrapper
            await _snapshotLock.WaitAsync(cancellationToken);
            string snapShotId = Guid.NewGuid().ToString();

            // Since events *should* be immutable, copying individual events by reference shouldn't be an issue
            var snapshot = new InMemoryEventStore();
            await CopyTo(snapshot);
            _snapshots.TryAdd(snapShotId, snapshot);

            return snapShotId;
        }
        finally
        {
            _snapshotLock.Release();
        }
    }

    /// <summary>
    /// Restores the event store from a previously taken snapshot
    /// </summary>
    /// <param name="snapshotId">The snapshot identifier</param>
    /// <param name="deleteSnapshotOnRestore">Whether to remove the snapshot once it has been restored</param>
    /// <returns></returns>
    public async Task RestoreFromSnapshot(string snapshotId, bool deleteSnapshotOnRestore, CancellationToken cancellationToken = default)
    {
        try
        {
            await _snapshotLock.WaitAsync(cancellationToken);
            _streams = _snapshots[snapshotId]._streams;

            if (deleteSnapshotOnRestore)
            {
                _snapshots.TryRemove(snapshotId, out _);
            }
        }
        finally
        {
            _snapshotLock.Release();
        }
    }
}