using MediatR;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using O9d.Guard;

#if NETSTANDARD2_1
using System.Diagnostics;
#endif

namespace Odyssey.EventConsumer;

internal sealed class CosmosEventConsumer : IHostedService
{
    private readonly CosmosEventConsumerOptions _eventConsumerOptions;
    private readonly CosmosClient _cosmosClient;
    private readonly ILogger _logger;
    private ChangeFeedProcessor? _changeFeedProcessor;
    private readonly IServiceProvider _serviceProvider;
    private readonly JsonSerializer _serializer = JsonSerializer.Create(SerializerSettings.Default);
    private readonly Dictionary<string, Type> TypeMap = new();

    public CosmosEventConsumer(
        IOptions<CosmosEventConsumerOptions> eventConsumerOptions,
        CosmosClient cosmosClient,
        ILogger<CosmosEventConsumer> logger,
        IServiceProvider serviceProvider,
        TypeMap typeMap)
    {
        _eventConsumerOptions = eventConsumerOptions.Value.NotNull();
        _cosmosClient = cosmosClient.NotNull();
        _logger = logger.NotNull();
        _serviceProvider = serviceProvider.NotNull();
        TypeMap = typeMap.NotNull().Value.NotNull();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Event Consumer");

        var leaseDatabaseId = string.IsNullOrWhiteSpace(_eventConsumerOptions.LeaseDatabaseId) ?
            _eventConsumerOptions.DatabaseId : _eventConsumerOptions.LeaseDatabaseId;

        var database = _cosmosClient.GetDatabase(leaseDatabaseId);

        // Container reference with creation if it does not already exist
        var leaseContainer = await database.CreateContainerIfNotExistsAsync(
            id: _eventConsumerOptions.LeaseContainerId,
            partitionKeyPath: "/id", // lease partition ley must be id or partitionKey
            cancellationToken: cancellationToken
        );

#if NET5_0_OR_GREATER
        var instanceName = $"{Environment.MachineName}:{Environment.ProcessId}";
#else
        var instanceName = $"{Environment.MachineName}:{Process.GetCurrentProcess().Id}";
#endif
        _logger.LogInformation("Starting Change Feed Processor {Instance}@{ProcessorName}", instanceName, _eventConsumerOptions.ProcessorName);

        // Ref https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/change-feed-processor?tabs=dotnet
        _changeFeedProcessor = _cosmosClient.GetContainer(_eventConsumerOptions.DatabaseId, _eventConsumerOptions.ContainerId)
            .GetChangeFeedProcessorBuilder<CosmosEvent>(processorName: _eventConsumerOptions.ProcessorName, onChangesDelegate: HandleChangesAsync)
                .WithLeaseAcquireNotification(OnLeaseAcquiredAsync)
                .WithLeaseReleaseNotification(OnLeaseReleaseAsync)
                .WithErrorNotification(OnErrorAsync)
                .WithInstanceName(instanceName)
                .WithLeaseContainer(leaseContainer)
                .Build();

        await _changeFeedProcessor.StartAsync();
    }

    public Task OnErrorAsync(string leaseToken, Exception exception)
    {
        if (exception is ChangeFeedProcessorUserException userException)
        {
            _logger.LogError(userException, "Lease {leaseToken} processing failed with unhandled exception from user delegate", leaseToken);
        }
        else
        {
            _logger.LogError(exception, "Lease {leaseToken} failed", leaseToken);
        }

        return Task.CompletedTask;
    }

    public Task OnLeaseReleaseAsync(string leaseToken)
    {
        _logger.LogInformation("Lease {leaseToken} is released and processing is stopped", leaseToken);
        return Task.CompletedTask;
    }

    public Task OnLeaseAcquiredAsync(string leaseToken)
    {
        _logger.LogInformation("Lease {leaseToken} is acquired and will start processing", leaseToken);
        return Task.CompletedTask;
    }

    private async Task HandleChangesAsync(IReadOnlyCollection<CosmosEvent> changes, CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested) // What can trigger this cancellation token?
        {
            _logger.LogInformation("Received {EventCount} events", changes.Count);

            foreach (var @event in changes)
            {
                _logger.LogInformation("Received event {EventType} {EventId}", @event.EventType, @event.Id);

                if (TypeMap.TryGetValue(@event.EventType, out Type? eventType))
                {
                    var typedEvent = @event.Data.ToObject(eventType, _serializer);

                    if (typedEvent is not null)
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var mediator = scope.ServiceProvider.GetService<IMediator>().NotNull();
                        await mediator.Publish(typedEvent, cancellationToken);
                        _logger.LogInformation("Published event {EventType} {EventId}", @event.EventType, @event.Id);
                    }
                    else
                    {
                        _logger.LogError("Published event {EventType} cannot be deserialized to {Type}", @event.EventType, eventType.Name);
                    }
                }
            }
        }
    }

    public async Task StopAsync(CancellationToken _)
    {
        _logger.LogInformation("Stopping Event Consumer");

        if (_changeFeedProcessor != null)
        {
            await _changeFeedProcessor.StopAsync();
        }
    }
}
