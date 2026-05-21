var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var queues = storage.AddQueues("queues");
var tables = storage.AddTables("tables");
// Blob service shared by two containers with distinct lifecycle policies:
//   "edict-state"       — grain state on the blob substrate (live data,
//                         no operator-set retention). Created on first use
//                         by Orleans's AzureBlobGrainStorage provider.
//   "edict-claim-check" — append-only oversize-event spill
//                         (operator-set retention). Created on first use
//                         by AzureBlobClaimCheckStore.CreateAsync.
var blobs = storage.AddBlobs("blobs");

builder.AddProject<Projects.Sample_Silo>("silo")
    .WithReference(queues)
    .WithReference(tables)
    .WithReference(blobs);
builder.AddProject<Projects.Sample_Api>("api")
    .WithReference(tables);

await builder.Build().RunAsync();
