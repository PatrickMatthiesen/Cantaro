// #:sdk Aspire.AppHost.Sdk@13.1.0-preview.1.25567.3
// #:package Aspire.Hosting.JavaScript@13.1.0-preview.1.25567.3

var builder = DistributedApplication.CreateBuilder(args);

var youtubeClientId = builder.AddParameter("YouTubeClientId", secret: true);
var youtubeClientSecret = builder.AddParameter("YouTubeClientSecret", secret: true);
var aniListClientId = builder.AddParameter("AniListClientId", secret: true);
var aniListClientSecret = builder.AddParameter("AniListClientSecret", secret: true);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithContainerName("cantaro-postgres")
    .WithHostPort(5432);
var db = postgres.AddDatabase("cantaro-db");

// Optionally, add pgAdmin for database management (runs in a separate container)
// var pgAdmin = postgres.WithPgAdmin();

var migrationService = builder.AddProject<Projects.Cantaro_MigrationService>("migration-service")
    .WithReference(db)
    .WaitFor(db);

// Add API service
var api = builder.AddProject<Projects.Cantaro_Api>("api")
    .WithEnvironment("YouTube:ClientId", youtubeClientId)
    .WithEnvironment("YouTube:ClientSecret", youtubeClientSecret)
    .WithEnvironment("AniList:ClientId", aniListClientId)
    .WithEnvironment("AniList:ClientSecret", aniListClientSecret)
    .WithReference(migrationService)
    .WaitForCompletion(migrationService)
    .WithReference(db);

// Add frontend
#pragma warning disable ASPIRECERTIFICATES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
var frontend = builder.AddViteApp("web", "../Cantaro.Web")
    .WithReference(api)
    .WaitFor(api)
    // .WithEndpoint("http", c => { c.IsExternal = true; c.Port = 8080; c.TargetPort = 5173; c.UriScheme = "http"; })
    // .WithHttpsEndpoint(port: 8443, targetPort: 5173, name: "https")
    .WithHttpsDeveloperCertificate();
#pragma warning restore ASPIRECERTIFICATES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

// Add frontend reference to API for OAuth redirects
api.WithReference(frontend);

builder.AddBunApp("browser-extension", "../Cantaro.BrowserExtension", entryPoint: "dev")
    .WithReference(api)
    .WithExplicitStart();

builder.Build().Run();
