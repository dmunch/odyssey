
namespace Odyssey.Tests;

using static Utils;

public class EventStoreExtensionsTests
{
    [Fact]
    public async Task Copies_streams_within_same_store()
    {
        var store = new InMemoryEventStore();
        string streamId = CreateStreamId();

        await store.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.NoStream);

        string newStreamId = CreateStreamId();
        var result = await store.CopyStream(streamId, newStreamId);

        result.Value.ShouldBeOfType<Success>();
        var events = await store.ReadStream(newStreamId, ReadDirection.Backwards, StreamPosition.Start);
        events.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Copies_streams_to_different_store()
    {
        var store = new InMemoryEventStore();
        string streamId = CreateStreamId();
        await store.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.NoStream);


        var target = new InMemoryEventStore();
        var result = await store.CopyStream(streamId, target);

        result.Value.ShouldBeOfType<Success>();
        var events = await target.ReadStream(streamId, ReadDirection.Backwards, StreamPosition.Start);
        events.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Copies_streams_to_different_store_and_stream_id()
    {
        var store = new InMemoryEventStore();
        string streamId = CreateStreamId();
        await store.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.NoStream);


        var target = new InMemoryEventStore();
        var targetStreamId = CreateStreamId();
        var result = await store.CopyStream(streamId, target, targetStreamId);

        result.Value.ShouldBeOfType<Success>();
        var events = await target.ReadStream(targetStreamId, ReadDirection.Backwards, StreamPosition.Start);
        events.Count().ShouldBe(1);
    }
}