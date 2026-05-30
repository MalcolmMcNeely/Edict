var builder = DistributedApplication.CreateBuilder(args);

// Kafka broker via Confluent's confluent-local image (Aspire default). Kafbat
// UI sidecars in as the topic/consumer-group inspection surface — first-party
// Aspire integration, no extra package. Mirrors the role Azure Storage
// Explorer plays implicitly against Azurite in the Azure sample.
var kafka = builder.AddKafka("kafka")
    .WithKafkaUI();

// Postgres + pgAdmin — single named database "appdb" carrying the silo's
// grain state, dead-letter projection, claim-check spill, and every
// projection-row table that EdictPostgresPersistence + the consumer
// projection builders create on demand. AddEdictPostgresPersistence runs
// the idempotent DDL bootstrap on first silo start.
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();
var appdb = postgres.AddDatabase("appdb");

var silo = builder.AddProject<Projects.Sample_KafkaPostgres_Silo>("silo")
    .WithReference(kafka).WaitFor(kafka)
    .WithReference(appdb).WaitFor(postgres)
    // The silo exposes /health that flips to Healthy only after Orleans
    // crosses ServiceLifecycleStage.Active. Web's WaitFor(silo) below honors
    // this so the Web's Orleans client cannot race the gateway-open moment
    // and surface a ConnectionFailedException on the dashboard.
    .WithHttpHealthCheck("/health");
builder.AddProject<Projects.Sample_KafkaPostgres_Web>("web")
    .WithReference(appdb).WaitFor(postgres)
    .WaitFor(silo);

await builder.Build().RunAsync();
