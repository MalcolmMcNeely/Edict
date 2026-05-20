var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var queues = storage.AddQueues("queues");
var tables = storage.AddTables("tables");
// Claim-check escape hatch for oversized events (ADR 0024). The container
// itself is created on first use by AzureBlobClaimCheckStore.CreateAsync.
var blobs = storage.AddBlobs("blobs");

builder.AddProject<Projects.Sample_Silo>("silo")
    .WithReference(queues)
    .WithReference(tables)
    .WithReference(blobs);
builder.AddProject<Projects.Sample_Api>("api")
    .WithReference(tables);

await builder.Build().RunAsync();
