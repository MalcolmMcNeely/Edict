var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Sample_Silo>("silo");
builder.AddProject<Projects.Sample_Api>("api");

await builder.Build().RunAsync();
