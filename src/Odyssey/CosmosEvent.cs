namespace Odyssey;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal sealed class CosmosEvent
{
    [JsonProperty("id")]
    public string Id { get; set; } = null!;

    [JsonProperty("stream_id")] // PK
    public string StreamId { get; set; } = null!;

    [JsonProperty("event_id")]
    public Guid EventId { get; set; }

    [JsonProperty("event_type")]
    public string EventType { get; set; } = null!;

    [JsonProperty("data")]
    public JObject Data { get; set; } = null!;

    [JsonProperty("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = null!;

    [JsonProperty("event_number")]
    public ulong EventNumber { get; set; }


    public EventData ToEventData(JsonSerializer serializer)
    {
        if (!Metadata.TryGetValue(MetadataFields.ClrType, out var clrTypeValue)
            || clrTypeValue is not string typeName)
        {
            throw new ArgumentException($"Item {Id} is missing the required {MetadataFields.ClrType} metadata value");
        }

        // TODO implement type cache
        Type? clrType = Type.GetType(typeName);

        if (clrType is null)
        {
            throw new ArgumentException($"The type {typeName} cannot be resolved");
        }

        return new EventData(
            EventId,
            EventType,
            Data.ToObject(clrType, serializer),
            Metadata
        );
    }

    public static CosmosEvent FromEventData(string streamId, ulong eventNumber, EventData @event, JsonSerializer serializer)
        => new()
        {
            Id = $"{eventNumber}@{streamId}",
            EventId = @event.Id,
            StreamId = streamId,
            EventType = @event.EventType,
            Data = JObject.FromObject(@event.Data, serializer),
            EventNumber = eventNumber,
            Metadata = GetMetadata(@event)
        };

    private static Dictionary<string, object> GetMetadata(EventData @event)
    {
        var metadata = @event.Metadata ?? new Dictionary<string, object>();
        metadata[MetadataFields.ClrType] = @event.Data.GetType().AssemblyQualifiedName!; // TODO allow a type map to be provided

        return metadata;
    }
}