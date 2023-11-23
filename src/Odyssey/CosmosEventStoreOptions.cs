namespace Odyssey;

using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

public class CosmosEventStoreOptions
{
    public CosmosEventStoreOptions()
    {
        DatabaseId = "odyssey";
        AutoCreateDatabase = true;
        ContainerId = "events";
        AutoCreateContainer = true;
        TypeResolver = TypeResolvers.UsingClrQualifiedTypeMetadata;
        UnresolvedTypeStrategy = UnresolvedTypeStrategies.Throw;
        EventSerializerSettings = SerializerSettings.Default;
    }

    public string DatabaseId { get; set; }
    public bool AutoCreateDatabase { get; set; }
    public string ContainerId { get; set; }
    public bool AutoCreateContainer { get; set; }
    public ThroughputProperties? DatabaseThroughputProperties { get; set; }
    public TypeResolver TypeResolver { get; set; }
    public UnresolvedTypeStrategy UnresolvedTypeStrategy { get; set; }

    /// <summary>
    /// Gets or sets the settings used to serialize event data. Defaults to <see cref="SerializerSettings.Default"/> 
    /// </summary>
    /// <value></value>
    public JsonSerializerSettings EventSerializerSettings { get; set; }
}