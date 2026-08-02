// #:sdk Aspire.AppHost.Sdk@13.5.0-preview.1.26319.13
// #:package Aspire.Hosting.JavaScript@13.5.0-preview.1.26319.13

using Cantaro.Aspire.Hosting.Garage;

var builder = DistributedApplication.CreateBuilder(args);

var dockerEnv = builder.AddDockerComposeEnvironment("docker-compose");

var youtubeClientId = builder.AddParameter("YouTubeClientId", secret: true);
var youtubeClientSecret = builder.AddParameter("YouTubeClientSecret", secret: true);
var spotifyClientId = builder.AddParameter("SpotifyClientId", secret: true);
var spotifyClientSecret = builder.AddParameter("SpotifyClientSecret", secret: true);
var aniListClientId = builder.AddParameter("AniListClientId", secret: true);
var aniListClientSecret = builder.AddParameter("AniListClientSecret", secret: true);
var extensionAuthJwtSigningKey = builder.ExecutionContext.IsRunMode
    ? builder.AddParameter("ExtensionAuthJwtSigningKey", "Cantaro.ExtensionAuth.Development.Signing.Key.2026.04.26", true)
    : builder.AddParameter("ExtensionAuthJwtSigningKey", secret: true);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("cantaro-postgres-data")
    .WithHostPort(5432);
if (builder.ExecutionContext.IsRunMode) {
    postgres.WithContainerName("cantaro-postgres");
}

var db = postgres.AddDatabase("cantaro-db");

var garage = builder.AddGarage("garage")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithVolume("cantaro-garage-data", GarageResource.DataPath);
var avatars = garage.AddBucket("avatars", "cantaro-avatars");
var garageProvisioner = builder.AddProject<Projects.Cantaro_GarageProvisioner>("garage-provisioner")
    .WithReference(garage)
    .WithReference(avatars)
    .WaitForStart(garage);

// Optionally, add pgAdmin for database management (runs in a separate container)
// var pgAdmin = postgres.WithPgAdmin();

var migrationService = builder.AddProject<Projects.Cantaro_MigrationService>("migration-service")
    .WithReference(db)
    .WaitFor(db);

// Add API service
var api = builder.AddProject<Projects.Cantaro_Api>("api")
    .WithEnvironment("YouTube:ClientId", youtubeClientId)
    .WithEnvironment("YouTube:ClientSecret", youtubeClientSecret)
    .WithEnvironment("Spotify:ClientId", spotifyClientId)
    .WithEnvironment("Spotify:ClientSecret", spotifyClientSecret)
    .WithEnvironment("AniList:ClientId", aniListClientId)
    .WithEnvironment("AniList:ClientSecret", aniListClientSecret)
    .WithEnvironment("ExtensionAuth:JwtSigningKey", extensionAuthJwtSigningKey);

if (builder.ExecutionContext.IsRunMode)
{
    api.WithEnvironment("Frontend:TrustLoopbackOrigins", "true");
    api.WithEnvironment("Frontend:TrustedHostSuffixes:0", ".ts.net");
    api.WithEnvironment("Frontend:TrustedHostSuffixes:1", ".dev.localhost");
}

if (builder.ExecutionContext.IsPublishMode)
{
    var frontendHttpsBaseUrl = builder.AddParameter("FrontendHttpsBaseUrl");
    api.WithEnvironment("Frontend:HttpsBaseUrl", frontendHttpsBaseUrl);
    api.WithEnvironment("Frontend:TrustedOrigins:0", frontendHttpsBaseUrl);

    api.PublishAsDockerComposeService((_, service) =>
    {
        service.Restart = "unless-stopped";
    });
}

api.WithReference(migrationService)
    .WaitForCompletion(migrationService)
    .WithReference(db)
    .WithReference(avatars)
    .WaitForCompletion(garageProvisioner);

if (builder.ExecutionContext.IsPublishMode)
{
    api.WithHttpsEndpoint(port: 7689, name: "https")
        .WithExternalHttpEndpoints();
}

// Add frontend
#pragma warning disable ASPIRECERTIFICATES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
var frontend = builder.AddViteApp("web", "../Cantaro.Web")
    .WithBun()
    .WithHttpEndpoint(port: 5173, name: "http")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpsDeveloperCertificate();

if (builder.ExecutionContext.IsPublishMode)
{
    // Keep the local Vite resource rooted in src/Cantaro.Web, but build the publish-only
    // static asset image from the repo root so Bun can resolve workspace packages.
    if (builder.TryCreateResourceBuilder<ContainerResource>("web", out var frontendContainer))
    {
        foreach (var dockerfile in frontendContainer.Resource.Annotations
                     .OfType<Aspire.Hosting.ApplicationModel.DockerfileBuildAnnotation>()
                     .ToArray())
        {
            frontendContainer.Resource.Annotations.Remove(dockerfile);
        }

        var contextPath = Path.GetFullPath("../..", builder.AppHostDirectory);
        var dockerfilePath = Path.GetFullPath("src/Cantaro.Web/Dockerfile", contextPath);

        frontendContainer.Resource.Annotations.Add(new Aspire.Hosting.ApplicationModel.DockerfileBuildAnnotation(
            contextPath,
            dockerfilePath,
            stage: null)
        {
            HasEntrypoint = false,
        });
    }
}
#pragma warning restore ASPIRECERTIFICATES001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

api.PublishWithContainerFiles(frontend, "/app/wwwroot");

// Keep the Vite development endpoint available to the API only during local runs.
if (builder.ExecutionContext.IsRunMode)
{
    frontend.WithEndpoint("http", endpoint => endpoint.TargetHost = "cantaro.dev.localhost");
    api.WithReference(frontend);
}

#pragma warning disable ASPIRETERMINAL001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
builder.AddJavaScriptApp("browser-extension", "../Cantaro.BrowserExtension")
    .WithBun()
    // no need for terminal if we get the fix merged:
    // https://github.com/wxt-dev/wxt/pull/2563
    .WithTerminal()
    .WithReference(api)
    .WithReference(frontend)
    .WithExplicitStart();
#pragma warning restore ASPIRETERMINAL001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

builder.Build().Run();
