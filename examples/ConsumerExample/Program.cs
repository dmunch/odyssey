using ConsumerExample;
using MediatR;
using Microsoft.Azure.Cosmos;
using Odyssey.EventConsumer;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    CosmosClient client = CreateClient(context.Configuration);
    services.AddSingleton(client);

    var typeMap = new Dictionary<string, Type>()
    {
        { "payment_initiated" , typeof(PaymentInitiated)},
    };
    services.AddOdysseyConsumer(typeMap, configurationSection: context.Configuration.GetSection(nameof(CosmosEventConsumerOptions)));

    services.AddMediatR(typeof(Program).Assembly);
});

IHost host = builder.Build();
await host.RunAsync();

static CosmosClient CreateClient(IConfiguration configuration)
{
    return new(
        accountEndpoint: configuration["Cosmos:Endpoint"],
        authKeyOrResourceToken: configuration["Cosmos:Token"]
    );
}