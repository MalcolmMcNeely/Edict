using Edict.Spike.Kafka.Adapter;
using Edict.Spike.Kafka.Contracts;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddFilter("Orleans", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting", LogLevel.Warning);

builder.Services.AddOrleans(silo =>
{
    silo.UseLocalhostClustering(siloPort: 11111, gatewayPort: 30000);
    silo.AddMemoryGrainStorageAsDefault();
    silo.AddMemoryGrainStorage("PubSubStore");

    silo.AddSpikeKafkaStreams(SpikeStreamNames.StreamProvider, o =>
    {
        o.BootstrapServers = builder.Configuration.GetConnectionString("kafka")
            ?? builder.Configuration["Kafka:BootstrapServers"]
            ?? "localhost:9092";
        o.Topic = SpikeStreamNames.OrdersTopic;
        o.PartitionCount = 4;
        o.ConsumerGroup = "spike-edict-silo";
    });
});

var host = builder.Build();
await host.RunAsync();
