using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using O9d.Guard;
using OneOf;
using OneOf.Types;

namespace Odyssey;

using AppendResult = OneOf<Success, UnexpectedStreamState>;

public sealed class CosmosEventStore : IEventStore
{
    private static readonly TransactionalBatchItemRequestOptions DefaultBatchOptions = new()
    {
        EnableContentResponseOnWrite = false
    };

    private readonly CosmosEventStoreOptions _options;
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger<CosmosEventStore> _logger;
    private readonly JsonSerializer _serializer;
    private Database _database = null!;
    private Container _container = null!;

    public CosmosEventStore(IOptions<CosmosEventStoreOptions> options, CosmosClient cosmosClient, ILoggerFactory loggerFactory)
    {
        options.NotNull(nameof(options));
        _options = options.Value.NotNull(nameof(options.Value));
        _cosmosClient = cosmosClient.NotNull();
        _logger = loggerFactory.NotNull().CreateLogger<CosmosEventStore>();

        _serializer = JsonSerializer.Create(_options.EventSerializerSettings ?? SerializerSettings.Default);
        _database = _cosmosClient.GetDatabase(_options.DatabaseId); // Does not guarantee existence
        _container = _database.GetContainer(_options.ContainerId); // Does not guarantee existence
    }

    // Could be abstracted
    public async Task Initialize(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_options.AutoCreateDatabase)
        {
            if (_options.DatabaseThroughputProperties is not null)
            {
                var databaseResponse = await _cosmosClient.CreateDatabaseIfNotExistsAsync(_options.DatabaseId, _options.DatabaseThroughputProperties, cancellationToken: cancellationToken);
                _database = databaseResponse.Database;
            }
            else
            {
                var databaseResponse = await _cosmosClient.CreateDatabaseIfNotExistsAsync(_options.DatabaseId, cancellationToken: cancellationToken);
                _database = databaseResponse.Database;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (_options.AutoCreateContainer)
        {
            var containerResponse = await CreateContainerIfNotExists(_database, _options.ContainerId, cancellationToken);
            _container = containerResponse.Container;
        }
    }

    private static Task<ContainerResponse> CreateContainerIfNotExists(Database database, string containerId, CancellationToken cancellationToken)
    {
        var containerProperties = new ContainerProperties()
        {
            Id = containerId,
            IndexingPolicy = new IndexingPolicy
            {
                IncludedPaths =
                    {
                        new IncludedPath {Path = "/*"},
                    },
                ExcludedPaths =
                    {
                        new ExcludedPath {Path = "/data/*"},
                        new ExcludedPath {Path = "/metadata/*"}
                    }
            },
            PartitionKeyPath = "/stream_id"
        };

        return database.CreateContainerIfNotExistsAsync(containerProperties, cancellationToken: cancellationToken);
    }

    public async Task<AppendResult> AppendToStream(string streamId, IReadOnlyList<EventData> events, StreamState expectedState, CancellationToken cancellationToken = default)
    {
        streamId.NotNullOrWhiteSpace();
        events.NotNull();

        if (events.Count == 0)
        {
            return Success.Instance;
        }

        _logger.LogDebug(
            "Append {EventCount} events to stream {StreamId} at position {ExpectedState}",
            events.Count,
            streamId,
            expectedState
        );

        var result = expectedState switch
        {
            { } when expectedState == StreamState.NoStream => AppendToNewStream(streamId, events, cancellationToken),
            { } when expectedState == StreamState.StreamExists => AppendToExistingStreamAnyVersion(streamId, events, cancellationToken),
            { } when expectedState == StreamState.Any => AppendToStreamAnyState(streamId, events, cancellationToken),
            _ => AppendToStreamAtVersion(streamId, events, expectedState, true, cancellationToken)
        };

        return await result;
    }

    /// <summary>
    /// Appends the provided events to a new stream
    /// We're able to validate that the stream does not exist by writing the first event 0@{StreamId}
    /// If the stream exists, this event would exist and therefore the CreateItem operation would fail
    /// </summary>
    private async Task<AppendResult> AppendToNewStream(string streamId, IReadOnlyList<EventData> events, CancellationToken cancellationToken)
    {
        TransactionalBatch batch = _container.CreateTransactionalBatch(new PartitionKey(streamId));

        for (int version = 0; version < events.Count; version++)
        {
            batch.CreateItem(CosmosEvent.FromEventData(streamId, version, events[version], _serializer), DefaultBatchOptions);
        }

        using var batchResponse = await batch.ExecuteAsync(cancellationToken);

        if (batchResponse.IsSuccessStatusCode)
        {
            return Success.Instance;
        }
        else if (batchResponse.StatusCode == HttpStatusCode.Conflict)
        {
            return new UnexpectedStreamState(StreamState.NoStream);
        }

        throw new CosmosException(batchResponse.ErrorMessage, batchResponse.StatusCode, 0, batchResponse.ActivityId, batchResponse.RequestCharge);
    }


    /// <summary>
    /// To append to an *existing* stream at any state we need to first obtain the current version (must be >= 0)
    /// Then we can append using current version as expected version
    /// </summary>
    private async Task<AppendResult> AppendToExistingStreamAnyVersion(string streamId, IReadOnlyList<EventData> events, CancellationToken cancellationToken)
    {
        StreamState currentState = await GetCurrentState(streamId, cancellationToken);

        if (currentState == StreamState.NoStream)
        {
            return new UnexpectedStreamState(StreamState.StreamExists);
        }

        return await AppendToStreamAtVersion(streamId, events, currentState, false, cancellationToken);
    }

    /// <summary>
    /// To append to a stream in any state we need to obtain the current version (last event) of the stream
    /// </summary>
    private async Task<AppendResult> AppendToStreamAnyState(string streamId, IReadOnlyList<EventData> events, CancellationToken cancellationToken)
    {
        StreamState currentState = await GetCurrentState(streamId, cancellationToken);
        return await AppendToStreamAtVersion(streamId, events, currentState, false, cancellationToken);
    }

    /// <summary>
    /// Gets the current state of the stream
    /// </summary>
    private async Task<StreamState> GetCurrentState(string streamId, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT value COUNT(e.id) 
            FROM e
            WHERE e.stream_id = @stream_id
        ";

        var queryDefinition = new QueryDefinition(sql)
            .WithParameter("@stream_id", streamId);

        var options = new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(streamId),
        };

        using var eventsQuery = _container.GetItemQueryIterator<long>(queryDefinition, requestOptions: options);

        if (!eventsQuery.HasMoreResults)
        {
            return StreamState.NoStream;
        }

        long eventCount = (await eventsQuery.ReadNextAsync(cancellationToken)).SingleOrDefault();
        return StreamState.AtVersion(eventCount - 1); // 0 based index
    }

    /// <summary>
    /// Appends to a stream at an expected version
    /// We validate this by attempting to read {ExpectedVersion}@{StreamId} within the same transaction
    /// If this fails, the stream is in an unexpected state. There are two reasons why this may happen:
    ///     * The expected version does not exist
    ///     * The stream has been updated and one of the events to append would override existing events
    /// </summary>
    private async Task<AppendResult> AppendToStreamAtVersion(string streamId, IReadOnlyList<EventData> events, StreamState version, bool validateVersion, CancellationToken cancellationToken = default)
    {
        TransactionalBatch batch = _container.CreateTransactionalBatch(new PartitionKey(streamId));

        var transactionalBatchItemRequestOptions = new TransactionalBatchItemRequestOptions
        {
            EnableContentResponseOnWrite = false // Don't return the event data in the response
        };

        // If we have already validated that the version exists (e.g. Appending in any state)
        // we can skip reading the item within the batch
        if (validateVersion)
        {
            // Attempt to read the event at the expected revision
            batch.ReadItem(CosmosEvent.GenerateId(version, streamId), new TransactionalBatchItemRequestOptions { EnableContentResponseOnWrite = false });
        }

        long newVersion = version;
        for (int index = 0; index < events.Count; index++)
        {
            batch.CreateItem(CosmosEvent.FromEventData(streamId, ++newVersion, events[index], _serializer), transactionalBatchItemRequestOptions);
        }

        using var batchResponse = await batch.ExecuteAsync(cancellationToken);

        if (batchResponse.IsSuccessStatusCode)
        {
            return Success.Instance;
        }
        else if (batchResponse.StatusCode == HttpStatusCode.Conflict)
        {
            return new UnexpectedStreamState(version);
        }

        throw new CosmosException(batchResponse.ErrorMessage, batchResponse.StatusCode, 0, batchResponse.ActivityId, batchResponse.RequestCharge);
    }

    public async Task<IReadOnlyCollection<EventData>> ReadStream(string streamId, ReadDirection direction, StreamPosition position, CancellationToken cancellationToken = default)
    {
        streamId.NotNullOrWhiteSpace();

        const string ForwardsQuery = @"
            SELECT VALUE e
            FROM e
            WHERE e.stream_id = @stream_id
            ORDER BY e.event_number ASC
        ";

        const string BackwardsQuery = @"
            SELECT VALUE e
            FROM e
            WHERE e.stream_id = @stream_id
            ORDER BY e.event_number DESC
        ";

        var queryDefinition = new QueryDefinition(direction == ReadDirection.Backwards ? BackwardsQuery : ForwardsQuery)
            .WithParameter("@stream_id", streamId);

        var options = new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(streamId)
        };

        using var eventsQuery = _container.GetItemQueryIterator<CosmosEvent>(queryDefinition, requestOptions: options);

        var events = new List<EventData>();

        while (eventsQuery.HasMoreResults)
        {
            var response = await eventsQuery.ReadNextAsync(cancellationToken);

            foreach (var @event in response)
            {
                EventData eventData = ResolveEvent(@event);

                events.Add(eventData);
            }
        }

        return events.AsReadOnly();
    }

    public async Task<OneOf<EventData, NotFound>> ReadStreamEvent(string streamId, long eventNumber, CancellationToken cancellationToken = default)
    {
        streamId.NotNullOrWhiteSpace();
        if (eventNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(eventNumber), "Event number cannot be negative");
        }

        try
        {
            ItemResponse<CosmosEvent> itemResponse = await _container.ReadItemAsync<CosmosEvent>(
                CosmosEvent.GenerateId(eventNumber, streamId),
                new PartitionKey(streamId),
                cancellationToken: cancellationToken);

            return ResolveEvent(itemResponse.Resource);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new NotFound();
        }
    }

    private EventData ResolveEvent(CosmosEvent @event)
    {
        Type? eventType = _options.TypeResolver.Invoke(@event.Id, @event.Metadata);

        return eventType is not null
            ? @event.ToEventData(eventType, _serializer)
            : _options.UnresolvedTypeStrategy.Invoke(@event);
    }
}