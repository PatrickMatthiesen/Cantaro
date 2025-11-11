var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

var db = postgres.AddDatabase("cantaro-db");

// Add API service
var api = builder.AddProject<Projects.Cantaro_Api>("api")
    .WithReference(db);

// Add frontend
var frontend = builder.AddNpmApp("web", "../Cantaro.Web")
    .WithReference(api)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
