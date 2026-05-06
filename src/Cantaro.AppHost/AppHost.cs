// #:sdk Aspire.AppHost.Sdk@13.1.0-preview.1.25567.3
// #:package Aspire.Hosting.JavaScript@13.1.0-preview.1.25567.3

var builder = DistributedApplication.CreateBuilder(args);

var dockerEnv = builder.AddDockerComposeEnvironment("docker-compose");

var youtubeClientId = builder.AddParameter("YouTubeClientId", secret: true);
var youtubeClientSecret = builder.AddParameter("YouTubeClientSecret", secret: true);
var aniListClientId = builder.AddParameter("AniListClientId", secret: true);
var aniListClientSecret = builder.AddParameter("AniListClientSecret", secret: true);
var extensionAuthJwtSigningKey = builder.ExecutionContext.IsRunMode
    ? builder.AddParameter("ExtensionAuthJwtSigningKey", "Cantaro.ExtensionAuth.Development.Signing.Key.2026.04.26", true)
    : builder.AddParameter("ExtensionAuthJwtSigningKey", secret: true);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithHostPort(5432);
if (builder.ExecutionContext.IsRunMode) {
    postgres.WithContainerName("cantaro-postgres");
} else {
    postgres.WithDataVolume("cantaro-postgres-data");
}

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
    .WithEnvironment("ExtensionAuth:JwtSigningKey", extensionAuthJwtSigningKey)
    .WithReference(migrationService)
    .WaitForCompletion(migrationService)
    .WithReference(db);

if (builder.ExecutionContext.IsPublishMode)
{
    api.WithHttpsEndpoint(port: 7689, name: "http")
        .WithExternalHttpEndpoints();
}

// Add frontend
#pragma warning disable ASPIRECERTIFICATES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
var frontend = builder.AddViteApp("web", "../Cantaro.Web")
    .WithBun()
    .WithReference(api)
    .WaitFor(api)
    .WithHttpsDeveloperCertificate();
#pragma warning restore ASPIRECERTIFICATES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

api.PublishWithContainerFiles(frontend, "/app/wwwroot");

// Keep the Vite development endpoint available to the API only during local runs.
if (builder.ExecutionContext.IsRunMode)
{
    api.WithReference(frontend);
}

builder.AddBunApp("browser-extension", "../Cantaro.BrowserExtension", entryPoint: "dev")
    .WithReference(api)
    .WithExplicitStart();

builder.Build().Run();
