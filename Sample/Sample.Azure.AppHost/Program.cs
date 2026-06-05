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

var silo = builder.AddProject<Projects.Sample_Azure_Silo>("silo")
    .WithReference(queues).WaitFor(storage)
    .WithReference(tables)
    .WithReference(blobs)
    // The silo exposes /health that flips to Healthy only after Orleans
    // crosses ServiceLifecycleStage.Active. Web's WaitFor(silo) below honors
    // this so the Web's Orleans client cannot race the gateway-open moment
    // and surface a ConnectionFailedException on the dashboard.
    .WithHttpHealthCheck("/health");
var web = builder.AddProject<Projects.Sample_Azure_Web>("web")
    .WithReference(tables).WaitFor(storage)
    .WithReference(blobs)
    .WaitFor(silo);

// The silo POSTs notifications to the Web-hosted sink, so it needs the Web's
// address via service discovery. Deliberately no WaitFor: the Web already
// WaitFor(silo) above, and a mutual wait would deadlock startup. The notifier
// swallows the unreachable first POSTs that race the Web coming up.
silo.WithReference(web);

await builder.Build().RunAsync();
