using Shouldly;

namespace Odyssey.Tests;

public class UnresolvedTypeStrategyTests
{
    [Fact]
    public void Default_strategy_throws()
    {
        var strategy = new CosmosEventStoreOptions().UnresolvedTypeStrategy;

        var deserializedEvent = new CosmosEvent
        {
            StreamId = "stream",
            Id = "0",
            EventId = Guid.NewGuid(),
            EventType = "BadEvent",
            Metadata = new Dictionary<string, object>(),
            Data = new Newtonsoft.Json.Linq.JObject()
        };

        Should.Throw<ArgumentException>(() => strategy.Invoke(deserializedEvent));
    }

    [Fact]
    public void Skip_strategy_returns_unresolved_event()
    {
        var strategy = UnresolvedTypeStrategies.Skip;

        var deserializedEvent = new CosmosEvent
        {
            StreamId = "stream",
            Id = "0",
            EventId = Guid.NewGuid(),
            EventType = "BadEvent",
            Metadata = new Dictionary<string, object>(),
            Data = new Newtonsoft.Json.Linq.JObject()
        };

        var eventData = strategy.Invoke(deserializedEvent);
        eventData.Data.ShouldNotBeNull();
        eventData.Data.ShouldBeOfType<UnresolvedEvent>();
    }

    private class HiddenEvent { }
}