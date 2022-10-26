using Microsoft.AspNetCore.Http.Json;
using Microsoft.Azure.Cosmos;
using Minid;
using O9d.Json.Formatting;
using Odyssey;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<JsonOptions>(
    opt => opt.SerializerOptions.PropertyNamingPolicy = new JsonSnakeCaseNamingPolicy());

var app = builder.Build();

using CosmosClient client = CreateClient(builder.Configuration);
var eventStore = new EventStore(client, "odyssey", app.Services.GetRequiredService<ILoggerFactory>());

await eventStore.Initialize();


app.MapPost("/payments", async (PaymentRequest payment) =>
{
    var initiated = new PaymentInitiated(Id.NewId("pay"), payment.Amount, payment.Currency, payment.Reference);
    await eventStore.AppendToStream(initiated.Id.ToString(), new[] { Map(initiated) }, StreamState.NoStream);

    return Results.Ok(new
    {
        initiated.Id,
        Status = "initiated"
    });
});

app.MapPost("/payments/{id}/authorize", async (Id id) =>
{
    var authorized = new PaymentAuthorized(id, DateTime.UtcNow);
    // Pass the expected stream revision
    await eventStore.AppendToStream(authorized.Id.ToString(), new[] { Map(authorized) }, 0);

    return Results.Ok(new
    {
        authorized.Id,
        Status = "authorized"
    });
});

app.MapPost("/payments/{id}/refunds", async (Id id) =>
{
    var refunded = new PaymentRefunded(id, DateTime.UtcNow);
    // Add the event, regardless of the state/revision of stream
    await eventStore.AppendToStream(refunded.Id.ToString(), new[] { Map(refunded) }, StreamState.Any);

    return Results.Ok(new
    {
        refunded.Id,
        Status = "refunded"
    });
});

app.MapGet("/events/{id}", async (string id) =>
{
    var events = await eventStore.ReadStream(id, Direction.Forwards, StreamPosition.Start);
    return Results.Ok(events);
});

app.Run();


static EventData Map<TEvent>(TEvent @event)
    => new(Guid.NewGuid(), @event!.GetType().Name.ToSnakeCase(), @event);



static CosmosClient CreateClient(IConfiguration configuration)
{
    return new(
        accountEndpoint: configuration["Cosmos:Endpoint"],
        authKeyOrResourceToken: configuration["Cosmos:Token"]
    );
}

record PaymentRequest(int Amount, string Currency, string Reference);
record PaymentInitiated(Id Id, int Amount, string Currency, string Reference);
record PaymentAuthorized(Id Id, DateTime AuthorizedOn);
record PaymentRefunded(Id Id, DateTime RefundedOn);