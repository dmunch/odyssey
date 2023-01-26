namespace Odyssey.Tests;

using System.Collections.Immutable;
using Newtonsoft.Json;
using Shouldly;

public class TypeResolverTests
{
    private readonly JsonSerializer _serializer = JsonSerializer.Create(SerializerSettings.Default);

    [Fact]
    public void Default_resolver_resolves_using_metadata()
    {
        var cosmosEvent = CosmosEvent.FromEventData(
            "stream",
            0,
            new EventData(Guid.NewGuid(), "test_event", new TestEvent()),
            _serializer
        );

        Type? clrType = new CosmosEventStoreOptions().TypeResolver.Invoke(cosmosEvent.Id, cosmosEvent.Metadata);
        clrType.ShouldNotBeNull();

        var deserialized = cosmosEvent.ToEventData(clrType, _serializer);
        deserialized.Data.ShouldNotBeNull();
        deserialized.Data.ShouldBeOfType<TestEvent>();
    }

    [Fact]
    public void Can_resolve_with_typemap()
    {
        var cosmosEvent = CosmosEvent.FromEventData(
            "stream",
            0,
            new EventData(Guid.NewGuid(), "test_event", new TestEvent()),
            _serializer
        );

        var typeMap = new Dictionary<string, Type> {
            { nameof(TestEvent), typeof(SomeOtherEvent) }
        }.ToImmutableDictionary();

        var clrType = TypeResolvers.UsingTypeMap(typeMap).Invoke(cosmosEvent.Id, cosmosEvent.Metadata);

        var deserialized = cosmosEvent.ToEventData(clrType.ShouldNotBeNull(), _serializer);
        deserialized.Data.ShouldNotBeNull();
        deserialized.Data.ShouldBeOfType<SomeOtherEvent>();
    }

    public class TestEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    public class SomeOtherEvent
    {
        public Guid Id { get; set; }
    }
}