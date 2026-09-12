// #:sdk Aspire.AppHost.Sdk@13.5.0-preview.1.26319.13
// #:package Aspire.Hosting.JavaScript@13.5.0-preview.1.26319.13

using Garage.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var dockerEnv = builder.AddDockerComposeEnvironment("cantaro-compose")
    .WithDashboard(enabled: false)
    .ConfigureComposeFile(composeFile =>
    {
        composeFile.Name = "cantaro";
    });

// One Google OAuth client serves both sign-in and YouTube.
var googleClientId = builder.AddParameter("GoogleClientId", secret: true);
var googleClientSecret = builder.AddParameter("GoogleClientSecret", secret: true);
var spotifyClientId = builder.AddParameter("SpotifyClientId", secret: true);
var spotifyClientSecret = builder.AddParameter("SpotifyClientSecret", secret: true);
var aniListClientId = builder.AddParameter("AniListClientId", secret: true);
var aniListClientSecret = builder.AddParameter("AniListClientSecret", secret: true);
var myAnimeListClientId = builder.AddParameter("MyAnimeListClientId", secret: true);
var myAnimeListClientSecret = builder.AddParameter("MyAnimeListClientSecret", secret: true);
var animeScheduleId = builder.AddParameter("AnimeScheduleId");
var animeScheduleToken = builder.AddParameter("AnimeScheduleToken", secret: true);
var turnstileMode = builder.AddParameter("TurnstileMode", () => builder.Configuration["Parameters:TurnstileMode"] ?? "Auto");
var turnstileSiteKey = builder.AddParameter("TurnstileSiteKey", () => builder.Configuration["Parameters:TurnstileSiteKey"] ?? "");
var turnstileSecret = builder.AddParameter("TurnstileSecret", () => builder.Configuration["Parameters:TurnstileSecret"] ?? "", secret: true);
var turnstileHostname = builder.AddParameter("TurnstileHostname", () => builder.Configuration["Parameters:TurnstileHostname"] ?? "");
#pragma warning disable ASPIREINTERACTION001
var authenticationMode = builder.AddParameter("AuthenticationMode", () => builder.Configuration["Parameters:AuthenticationMode"] ?? "Local")
    .WithCustomInput(parameter => new()
    {
        Name = parameter.Name,
        Label = parameter.Name,
        InputType = InputType.Choice,
        Required = true,
        AllowCustomChoice = false,
        Options =
        [
            new("Local", "Local (password sign-in)"),
            new("Both", "Both (password and Google sign-in)"),
            new("GoogleOnly", "GoogleOnly (Google sign-in only)")
        ]
    });
#pragma warning restore ASPIREINTERACTION001
var trustedProxyIp = builder.AddParameter("TrustedProxyIp", () => builder.Configuration["Parameters:TrustedProxyIp"] ?? "", secret: true)
    .WithDescription("Comma-separated IP addresses of the proxies connecting directly to the API. Leave empty to disable forwarded-header trust.");
var extensionAuthJwtSigningKey = builder.ExecutionContext.IsRunMode
    ? builder.AddParameter("ExtensionAuthJwtSigningKey", "Cantaro.ExtensionAuth.Development.Signing.Key.2026.04.26", true)
    : builder.AddParameter("ExtensionAuthJwtSigningKey", secret: true);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("cantaro-postgres-data")
    .WithHostPort(5432)
    .PublishAsDockerComposeService((_, service) =>
    {
        service.Restart = "unless-stopped";
    });

if (builder.ExecutionContext.IsRunMode) {
    postgres.WithContainerName("cantaro-postgres");
}

var db = postgres.AddDatabase("cantaro-db");

var garage = builder.AddGarage("garage", new GarageResourceOptions { ConfigPath = "Garage/garage.toml" })
    // Clean up the development container on shutdown; the named volume retains its data.
    .WithLifetime(ContainerLifetime.Session)
    .WithDataVolume("cantaro-garage-data")
    .PublishAsDockerComposeService((_, service) =>
    {
        service.Restart = "unless-stopped";
    });
var avatars = garage.AddBucket("avatars", "cantaro-avatars");

// Optionally, add pgAdmin for database management (runs in a separate container)
// var pgAdmin = postgres.WithPgAdmin();

var migrationService = builder.AddProject<Projects.Cantaro_MigrationService>("migration-service")
    .WithReference(db)
    .WaitFor(db);

// Add API service
var api = builder.AddProject<Projects.Cantaro_Api>("api")
    .WithEnvironment("YouTube:ClientId", googleClientId)
    .WithEnvironment("YouTube:ClientSecret", googleClientSecret)
    .WithEnvironment("Spotify:ClientId", spotifyClientId)
    .WithEnvironment("Spotify:ClientSecret", spotifyClientSecret)
    .WithEnvironment("AniList:ClientId", aniListClientId)
    .WithEnvironment("AniList:ClientSecret", aniListClientSecret)
    .WithEnvironment("MyAnimeList:ClientId", myAnimeListClientId)
    .WithEnvironment("MyAnimeList:ClientSecret", myAnimeListClientSecret)
    .WithEnvironment("AnimeSchedule:Id", animeScheduleId)
    .WithEnvironment("AnimeSchedule:Token", animeScheduleToken)
    .WithEnvironment("Turnstile:Mode", turnstileMode)
    .WithEnvironment("Turnstile:SiteKey", turnstileSiteKey)
    .WithEnvironment("Turnstile:Secret", turnstileSecret)
    .WithEnvironment("Turnstile:AllowedHostnames:0", turnstileHostname)
    .WithEnvironment("Authentication:Mode", authenticationMode)
    .WithEnvironment("Authentication:Google:ClientId", googleClientId)
    .WithEnvironment("Authentication:Google:ClientSecret", googleClientSecret)
    .WithEnvironment("RateLimiting:TrustedProxies:0", trustedProxyIp)
    .WithEnvironment("ExtensionAuth:JwtSigningKey", extensionAuthJwtSigningKey)
    .PublishAsDockerComposeService((_, service) =>
    {
        service.Restart = "unless-stopped";
    });

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
}

api.WithReference(migrationService)
    .WaitForCompletion(migrationService)
    .WithReference(db)
    .WithReference(avatars)
    .WaitFor(garage);

if (builder.ExecutionContext.IsPublishMode)
{
    api.WithHttpsEndpoint(port: 7689, name: "https")
        .WithExternalHttpEndpoints()
        .PublishAsDockerComposeService((_, service) =>
        {
            // Aspire also emits a container-only mapping that publishes a random
            // host port. Keep only the explicitly configured ingress port.
            service.Ports.RemoveAll(port => !port.Contains(':'));
        });
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
if (builder.ExecutionContext.IsRunMode)
{
    builder.AddJavaScriptApp("browser-extension", "../Cantaro.BrowserExtension")
        .WithBun()
        // no need for terminal if we get the fix merged:
        // https://github.com/wxt-dev/wxt/pull/2563
        .WithTerminal()
        .WithReference(api)
        .WithReference(frontend);
}
#pragma warning restore ASPIRETERMINAL001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

builder.Build().Run();
