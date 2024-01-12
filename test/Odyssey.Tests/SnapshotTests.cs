namespace Odyssey.Tests;

using static Utils;

public class SnapshotTests
{
    [Fact]
    public async Task Can_create_and_restore_snapshotsAsync()
    {
        var store = new InMemoryEventStore();

        var streamId = Guid.NewGuid().ToString();
        await store.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.NoStream);

        string snapShotId = await store.CreateSnapshot();

        await store.AppendToStream(streamId, new[] { MapEvent(new TestEvent()) }, StreamState.StreamExists);
        (await store.ReadStream(streamId, ReadDirection.Backwards, StreamPosition.Start)).Count.ShouldBe(2);

        await store.RestoreFromSnapshot(snapShotId, true);
        (await store.ReadStream(streamId, ReadDirection.Backwards, StreamPosition.Start)).Count.ShouldBe(1);
    }
}