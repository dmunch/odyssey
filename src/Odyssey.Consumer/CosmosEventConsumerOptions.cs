namespace Odyssey.EventConsumer;

public class CosmosEventConsumerOptions
{
    public string ProcessorName { get; set; } = "cosmos-event-consumer";
    public string DatabaseId { get; set; } = "odyssey";
    public string ContainerId { get; set; } = "events";
    public string LeaseContainerId { get; set; } = "leases";
    public string? LeaseDatabaseId { get; set; }
}
