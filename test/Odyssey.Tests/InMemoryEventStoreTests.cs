namespace Odyssey.Tests;
using static Utils;
using Shouldly;

public class InMemoryEventStoreTests
{
    private readonly InMemoryEventStore _eventStore;

    public InMemoryEventStoreTests()
    {
        _eventStore = new InMemoryEventStore();
    }

    [Fact]
    public async Task Can_append_and_read_events()
    {
        var streamId = Guid.NewGuid().ToString();

        var @event = new TestEvent();
        await _eventStore.AppendToStream(streamId, new[] { MapEvent(@event) }, StreamState.Any);

        var events = await _eventStore.ReadStream(streamId, ReadDirection.Forwards, StreamPosition.Start);
        events.Count.ShouldBe(1);
        events.First().Data.ShouldBeSameAs(@event);
    }

    [Fact]
    public async Task Returns_unexpected_when_stream_should_not_exist_but_does()
    {
        var streamId = Guid.NewGuid().ToString();
        await _eventStore.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.NoStream);

        var result = await _eventStore.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.NoStream);
        result.Value.ShouldBeOfType<UnexpectedStreamState>();
    }

    [Fact]
    public async Task Returns_unexpected_when_stream_should_exist_but_doesnt()
    {
        var streamId = Guid.NewGuid().ToString();
        var result = await _eventStore.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.StreamExists);
        result.Value.ShouldBeOfType<UnexpectedStreamState>();
    }

    [Fact]
    public async Task Returns_unexpected_stream_not_at_expected_version()
    {
        var streamId = Guid.NewGuid().ToString();

        await _eventStore.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.NoStream);
        await _eventStore.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.StreamExists);
        var result = await _eventStore.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.AtVersion(0));
        // Stream now at 1

        result.Value.ShouldBeOfType<UnexpectedStreamState>();
    }

    [Fact]
    public async Task Can_read_stream_backwards()
    {
        var streamId = Guid.NewGuid().ToString();
        await _eventStore.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.NoStream);
        await _eventStore.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.AtVersion(0));
        await _eventStore.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.AtVersion(1));

        var events = await _eventStore.ReadStream(streamId, ReadDirection.Backwards, StreamPosition.Start);

        events.First().EventNumber.ShouldBe(2);
        events.Last().EventNumber.ShouldBe(0);
    }

    [Fact]
    public async Task Can_remove_stream()
    {
        var streamId = Guid.NewGuid().ToString();
        await _eventStore.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.NoStream);

        var events = await _eventStore.ReadStream(streamId, ReadDirection.Backwards, StreamPosition.Start);
        events.Count().ShouldBe(1);

        _eventStore.DeleteStream(streamId);
        events = await _eventStore.ReadStream(streamId, ReadDirection.Backwards, StreamPosition.Start);
        events.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Can_be_cloned()
    {
        var streamId = Guid.NewGuid().ToString();
        await _eventStore.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.NoStream);

        var clone = new InMemoryEventStore();
        await _eventStore.CopyTo(clone);

        var events = await _eventStore.ReadStream(streamId, ReadDirection.Backwards, StreamPosition.Start);
        events.Count().ShouldBe(1);
    }
}