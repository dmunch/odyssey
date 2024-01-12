namespace Odyssey;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using O9d.Guard;
using OneOf;
using OneOf.Types;

/// <summary>
/// Implementation of the event store that reads from files on disk
/// </summary>
public sealed class JsonFileEventStore : IEventStore, ICloneable
{
    private static readonly IReadOnlyCollection<EventData> EmptyStream = Array.Empty<EventData>();
    private readonly Dictionary<string, List<EventData>> _appendedEvents = new();
    private string _storagePath;
    private readonly JsonSerializer _serializer;
    private readonly TypeResolver _eventTypeResolver;

    public JsonFileEventStore(string storagePath)
    {
        _storagePath = storagePath.NotNull();
        _serializer = JsonSerializer.Create(SerializerSettings.Default);
        _eventTypeResolver = TypeResolvers.UsingClrQualifiedTypeMetadata;
    }

    public Task<OneOf<Success, UnexpectedStreamState>> AppendToStream(string streamId, IReadOnlyList<EventData> events, StreamState expectedState, CancellationToken cancellationToken = default)
    {
        if (expectedState != StreamState.NoStream)
        {
            throw new InvalidOperationException("Appending to an existing stream is not currently supported");
        }

        using var sw = new StreamWriter(GetStreamFilePath(streamId));
        using var writer = new JsonTextWriter(sw);
        _serializer.Serialize(writer, events);
        return Task.FromResult<OneOf<Success, UnexpectedStreamState>>(new Success());
    }

    public Task Initialize(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyCollection<EventData>> ReadStream(string streamId, ReadDirection direction, StreamPosition position, CancellationToken cancellationToken = default)
    {
        // TODO support json or jsonc
        string streamFilePath = GetStreamFilePath(streamId);

        if (!File.Exists(streamFilePath))
        {
            return EmptyStream;
        }

        string fileJson = await File.ReadAllTextAsync(streamFilePath);
        var fileEvents = JsonConvert.DeserializeObject<IEnumerable<JsonFileEvent>>(fileJson) ?? Array.Empty<JsonFileEvent>();

        var events = new List<EventData>();

        foreach (JsonFileEvent fileEvent in fileEvents)
        {
            EventData eventData = ResolveEvent(fileEvent);
            events.Add(eventData);
        }

        if (_appendedEvents.ContainsKey(streamId))
        {
            events.AddRange(_appendedEvents[streamId]);
        }

        return events.AsReadOnly();
    }

    private string GetStreamFilePath(string streamId) => Path.Combine(_storagePath, $"{streamId}.jsonc");

    public async Task<OneOf<EventData, NotFound>> ReadStreamEvent(string streamId, long eventNumber, CancellationToken cancellationToken = default)
    {
        var events = await ReadStream(streamId, ReadDirection.Forwards, StreamPosition.Start, cancellationToken);

        if (eventNumber > events.Count - 1)
        {
            return new NotFound();
        }

        return events.ElementAt((int)eventNumber);
    }

    public void ClearAppendedEvents() => _appendedEvents.Clear();

    private EventData ResolveEvent(JsonFileEvent @event)
    {
        Type? eventType = _eventTypeResolver.Invoke(@event.Id, @event.Metadata);

        return eventType is not null
            ? @event.ToEventData(eventType, _serializer)
            : throw new ArgumentException($"The CLR type for event {@event.EventType} cannot be resolved");
    }

    public async Task CopyTo(IEventStore target, CancellationToken cancellationToken = default)
    {
        target.NotNull();
        foreach (var streamId in GetStreamsFromDirectory())
        {
            var @events = await ReadStream(streamId, ReadDirection.Forwards, StreamPosition.Start, cancellationToken);
            await target.AppendToStream(streamId, @events.ToList(), StreamState.NoStream, cancellationToken);
        }
    }

    private IEnumerable<string> GetStreamsFromDirectory()
    {
        return Directory.GetFiles(_storagePath)
            .Select(path => Path.GetFileNameWithoutExtension(path));
    }

    public sealed class JsonFileEvent
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
        public long EventNumber { get; set; }

        // https://learn.microsoft.com/en-us/azure/cosmos-db/account-databases-containers-items#properties-of-an-item
        [JsonProperty("_ts")] // Unix time
        public long? Timestamp { get; set; }

        public EventData ToEventData(Type clrType, JsonSerializer serializer)
        {
            var eventData = new EventData(
                EventId,
                EventType,
                Data.ToObject(clrType, serializer)!,
                Metadata
            )
            {
                EventNumber = EventNumber
            };

            return eventData;
        }

        public static string GenerateId(long eventNumber, string streamId) => $"{eventNumber}@{streamId}";
    }
}
