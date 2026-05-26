var builder = DistributedApplication.CreateBuilder(args);

var kafka = builder.AddKafka("kafka")
    .WithKafkaUI();

builder.AddProject<Projects.Edict_Spike_Kafka_Silo>("silo")
    .WithReference(kafka)
    .WaitFor(kafka);

await builder.Build().RunAsync();
