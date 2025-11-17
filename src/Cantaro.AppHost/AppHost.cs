// #:sdk Aspire.AppHost.Sdk@13.1.0-preview.1.25567.3
// #:package Aspire.Hosting.JavaScript@13.1.0-preview.1.25567.3

var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres")
    .WithHostPort(5432)
    .WithPgAdmin();
var db = postgres.AddDatabase("cantaro-db");

// Add API service
var api = builder.AddProject<Projects.Cantaro_Api>("api")
    .WithReference(db)
    .WaitFor(db);

// Add frontend
var frontend = builder.AddViteApp("web", "../Cantaro.Web")
    .WithReference(api)
    .WaitFor(db);

builder.AddBunApp("browser-extension", "../Cantaro.BrowserExtension", entryPoint: "dev")
    .WithReference(api)
    .WithExplicitStart();

builder.Build().Run();
