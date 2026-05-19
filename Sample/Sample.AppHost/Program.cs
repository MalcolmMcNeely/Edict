var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var queues = storage.AddQueues("queues");
var tables = storage.AddTables("tables");

builder.AddProject<Projects.Sample_Silo>("silo")
    .WithReference(queues)
    .WithReference(tables);
builder.AddProject<Projects.Sample_Api>("api")
    .WithReference(tables);

await builder.Build().RunAsync();
