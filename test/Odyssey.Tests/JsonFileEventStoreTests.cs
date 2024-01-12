namespace Odyssey.Tests;

using O9d.Guard;
using static Utils;

public class JsonFileEventStoreTests
{
    [Fact]
    public async Task Can_read_json_file_events()
    {
        var eventStore = new JsonFileEventStore("event-streams");
        var streamId = "test-stream";

        var events = await eventStore.ReadStream(streamId, ReadDirection.Forwards, StreamPosition.Start);
        events.Count.ShouldBe(1);
        var eventData = events.First();

        var @event = eventData.Data.ShouldBeOfType<JsonEvent>();

        var expected = new JsonEvent("Did the thing");
        @event.ShouldBe(expected);
    }

    [Fact]
    public async Task Can_read_event_at_index()
    {
        var eventStore = new JsonFileEventStore("event-streams");
        var streamId = "test-stream";

        var result = await eventStore.ReadStreamEvent(streamId, 0);
        result.Value.ShouldBeOfType<EventData>().NotNull();
    }

    [Fact]
    public async void Can_clone()
    {
        var eventStore = new JsonFileEventStore("event-streams");
        var inMemStore = new InMemoryEventStore();

        await eventStore.CopyTo(inMemStore);
        var events = await inMemStore.ReadStream("test-stream", ReadDirection.Backwards, StreamPosition.Start);
        events.Count().ShouldBe(1);
    }

    [Fact]
    public async void Can_write_and_read_events()
    {
        var eventStore = new JsonFileEventStore("temp");
        await eventStore.Initialize();

        string streamId = CreateStreamId();
        var @event = new JsonEvent("some reference");
        await eventStore.AppendToStream(streamId, new[] { MapEvent(@event) }, StreamState.NoStream);

        var events = await eventStore.ReadStream(streamId, ReadDirection.Backwards, StreamPosition.Start);
        events.Count().ShouldBe(1);
    }
}

public record JsonEvent(string Reference);
