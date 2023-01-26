# Odyssey

Odyssey enables Azure Cosmos DB to be used as an Event Store.

## Quick Start

### Register Odyssey at startup:

```c#
builder.Services.AddOdyssey(cosmosClientFactory: _ => CreateClient(builder.Configuration));

static CosmosClient CreateClient(IConfiguration configuration)
{
    return new(
        accountEndpoint: configuration["Cosmos:Endpoint"],
        authKeyOrResourceToken: configuration["Cosmos:Token"]
    );
}
```

You can provide a factory to create and register the underlying `CosmosClient` instance as per the above example, otherwise you must register this yourself.

#### Initialization

If you want Odyssey to auto-create the database and/or container, call `IEventStore.Initialize` at startup:

```c#
await builder.Services.GetRequiredService<IEventStore>().Initialize();
```

### Take a dependency on `IEventStore`

```c#
app.MapPost("/payments", async (PaymentRequest payment, IEventStore eventStore) =>
{
    var initiated = new PaymentInitiated(Id.NewId("pay"), payment.Amount, payment.Currency, payment.Reference);

    var result = await eventStore.AppendToStream(initiated.Id.ToString(), new[] { Map(initiated) }, StreamState.NoStream);

    return result.Match(
        success => Results.Ok(new
        {
            initiated.Id,
            Status = "initiated"
        }),
        unexpected => Results.Conflict()
    );
});
```

## Configuration

By default Odyssey will attempt to create a Cosmos Database named `odyssey` and container named `events`.

You can control these settings as well as the auto-create settings using the .NET configuration system, for example, in `appsettings.json`:

```json
  "Odyssey": {
    "DatabaseId": "payments",
    "ContainerId": "payment-events",
    "AutoCreateDatabase": false,
    "AutoCreateContainer": false
  },
```

To initialize Odyssey with these settings, pass the relevant configuration section to Odyssey during initialization:

```c#
builder.Services.AddOdyssey(
    configureOptions: options => options.DatabaseThroughputProperties = ThroughputProperties.CreateAutoscaleThroughput(1000),
    cosmosClientFactory: _ => CreateClient(builder.Configuration),
    builder.Configuration.GetSection("Odyssey")
);

static CosmosClient CreateClient(IConfiguration configuration)
{
    return new(
        accountEndpoint: configuration["Cosmos:Endpoint"],
        authKeyOrResourceToken: configuration["Cosmos:Token"]
    );
}
```

Note that this also demonstrates how to specify the throughput properties of the created Container.

### Type Resolvers

By default, events are deserialized to the fully qualified type of the original event using the automatically generated metadata field `_clr_type`.

In some cases such as event consumers, you may not have access to the original assembly in which the event types reside. Instead, you can choose provide your own type map which will make use of the `_clr_type_name` (the _non_-qualified type name) to resolve the type, for example:

```c#
var typeMap = new Dictionary<string, Type> {
    { nameof(TestEvent), typeof(SomeOtherEvent) }
}.ToImmutableDictionary();

builder.Services.AddOdyssey(
    configureOptions: options => options.TypeResolver = TypeResolvers.UsingTypeMap(typeMap),
    cosmosClientFactory: _ => CreateClient(builder.Configuration),
    builder.Configuration.GetSection("Odyssey")
);
```

#### Handling unresolved types

In some cases it may not be possible or desirable to resolve an event's type. This could be the case for consumers where you wish to ignore certain events or in producers where an event has been deprecated.
The default strategy is to throw. This can be overridden by providing your own `UnresolvedTypeStrategy` or using the provided strategy, `UnresolvedTypeStrategy.Skip`:

```c#
builder.Services.AddOdyssey(
    configureOptions: options => options.UnresolvedTypeStrategy = UnresolvedTypeStrategy.Skip,
    cosmosClientFactory: _ => CreateClient(builder.Configuration),
    builder.Configuration.GetSection("Odyssey")
);
```

When using `Skip`, any event types that have failed to resolve using the provided `TypeResolver` will resolve to an instance of `UnresolvedEvent`.

Alternatively, if you are using the type map resolver above, you can specify a fallback type:

```c#
TypeResolvers.UsingTypeMap(typeMap, typeof(MyFallbackType))
```
