namespace Odyssey.EventConsumer;

using System;
using System.Linq;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using O9d.Guard;

public static class OdysseyConsumerServiceCollectionExtentions
{
    public static IServiceCollection AddOdysseyConsumer(
        this IServiceCollection services,
        Dictionary<string, Type> typeMap,
        Action<CosmosEventConsumerOptions>? configureOptions = null,
        Func<IServiceProvider, CosmosClient>? cosmosClientFactory = null,
        IConfiguration? configurationSection = null)
    {
        services.NotNull();

        if (cosmosClientFactory != null && !IsServiceRegistered<CosmosClient>(services))
        {
            services.AddSingleton(cosmosClientFactory);
        }

        if (configurationSection is not null)
        {
            services.Configure<CosmosEventConsumerOptions>(configurationSection);
        }

        services.AddSingleton(new TypeMap(typeMap));
        services.AddHostedService<CosmosEventConsumer>();
        configureOptions ??= (_ => { });

        services.Configure(configureOptions);

        return services;
    }

    private static bool IsServiceRegistered<TService>(IServiceCollection services)
        => services.Any(sd => sd.ServiceType == typeof(TService));
}
