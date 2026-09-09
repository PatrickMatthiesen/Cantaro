using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Cantaro.Api.Services.Lyrics;
using Cantaro.Api.Services.Spotify;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var extensionAuthOptions = builder.Configuration
    .GetSection(ExtensionAuthOptions.SectionName)
    .Get<ExtensionAuthOptions>() ?? new ExtensionAuthOptions();
var extensionJwtSigningKey = ExtensionAuthSigningKeyResolver.ResolveSigningKey(extensionAuthOptions);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add PostgreSQL database context via Aspire defaults.
builder.Configuration.ConfigurePostgresConnection("cantaro-db");
builder.AddNpgsqlDbContext<ApplicationDbContext>(connectionName: "cantaro-db");
builder.AddS3ObjectStorage("avatars");

// Add Data Protection for auth cookies and token encryption.
builder.Services
    .AddDataProtection()
    .SetApplicationName("Cantaro")
    .PersistKeysToDbContext<ApplicationDbContext>();

builder.Services.AddHttpContextAccessor();
builder.Services.Configure<FrontendUrlOptions>(builder.Configuration.GetSection(FrontendUrlOptions.SectionName));
builder.Services
    .AddOptions<SpotifyOptions>()
    .Bind(builder.Configuration.GetSection(SpotifyOptions.SectionName));
builder.Services
    .AddOptions<ExtensionAuthOptions>()
    .Bind(builder.Configuration.GetSection(ExtensionAuthOptions.SectionName))
    .ValidateDataAnnotations();
builder.Services
    .AddOptions<TrackMatchingOptions>()
    .Bind(builder.Configuration.GetSection(TrackMatchingOptions.SectionName))
    .ValidateDataAnnotations();
builder.Services
    .AddOptions<MusicSyncJobWorkerOptions>()
    .Bind(builder.Configuration.GetSection(MusicSyncJobWorkerOptions.SectionName))
    .ValidateDataAnnotations();
builder.Services
    .AddOptions<AniListOptions>()
    .Bind(builder.Configuration.GetSection(AniListOptions.SectionName));
builder.Services
    .AddOptions<MyAnimeListOptions>()
    .Bind(builder.Configuration.GetSection(MyAnimeListOptions.SectionName));
builder.Services
    .AddOptions<AnimeScheduleOptions>()
    .Bind(builder.Configuration.GetSection(AnimeScheduleOptions.SectionName));
builder.Services
    .AddOptions<LyricsOptions>()
    .Bind(builder.Configuration.GetSection(LyricsOptions.SectionName))
    .ValidateDataAnnotations();

// Register custom services
builder.Services.AddScoped<TokenEncryptionService>();
builder.Services.AddScoped<YouTubeService>();
builder.Services.AddScoped<YouTubePlaylistSyncService>();
builder.Services.AddScoped<PlaylistCanonicalReconciliationService>();
builder.Services.AddScoped<IPlatformService, YouTubePlatformService>();
builder.Services.AddHttpClient<SpotifyApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.spotify.com");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Cantaro/1.0 (+https://github.com/PatrickMatthiesen/Cantaro)");
});
builder.Services.AddSingleton<ISpotifyRetryDelay, SpotifyRetryDelay>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SpotifyTokenManager>();
builder.Services.AddSingleton<SpotifyCatalogTokenCache>();
builder.Services.AddScoped<SpotifyCatalogTokenProvider>();
builder.Services.AddScoped<SpotifyService>();
builder.Services.AddScoped<SpotifyPlaylistSyncService>();
builder.Services.AddScoped<TrackIdentityResolver>();
builder.Services.AddScoped<SpotifyTrackResolver>();
builder.Services.AddScoped<IPlatformService, SpotifyPlatformService>();
builder.Services.AddScoped<IPlatformRegistry, PlatformRegistry>();
builder.Services.AddScoped<IMediaProviderRegistry, MediaProviderRegistry>();
builder.Services.AddScoped<IFrontendUrlResolver, FrontendUrlResolver>();
builder.Services.AddMusicBrainzQueryClient();
builder.Services.AddAniListApiClient();
builder.Services.AddMyAnimeListApiClient();
builder.Services.AddHttpClient<AnimeScheduleApiClient>();
builder.Services.AddHttpClient("lrclib", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LyricsOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Cantaro/1.0 (+https://github.com/PatrickMatthiesen/Cantaro)");
});
builder.Services.AddSingleton<LyricsProviderCache>();
builder.Services.AddSingleton<ILyricsProvider, LrclibLyricsProvider>();
builder.Services.AddScoped<LyricsService>();
builder.Services.AddScoped<IMediaProvider, AniListMediaProvider>();
builder.Services.AddScoped<IMediaProvider, MyAnimeListMediaProvider>();
builder.Services.AddScoped<MediaLibraryImportService>();
builder.Services.AddScoped<MediaProviderInitialSyncService>();
builder.Services.AddSingleton<MediaProviderInitialSyncGate>();
builder.Services.AddScoped<MediaTitleRelationSyncService>();
builder.Services.AddScoped<MediaFranchiseGraphService>();
builder.Services.AddSingleton<MediaRelationGraphRefreshQueue>();
builder.Services.AddScoped<AnimeScheduleAvailabilitySyncService>();
builder.Services.AddSingleton<MediaLibraryImportQueue>();
builder.Services.AddScoped<MediaLibraryQueryService>();
builder.Services.AddScoped<MediaLibraryLinkService>();
builder.Services.AddScoped<MusicLibraryQueryService>();
builder.Services.AddScoped<CantaroSearchService>();
builder.Services.AddSingleton<MediaProviderSearchCache>();
builder.Services.AddScoped<MediaProviderOperationProcessor>();
builder.Services.AddScoped<MediaObservationMatchingService>();
builder.Services.AddScoped<MediaObservationRetentionService>();
builder.Services.AddScoped<MediaObservationLifecycleService>();
builder.Services.AddScoped<MediaProviderSeasonMappingService>();
builder.Services.AddScoped<MediaObservationProgressService>();
builder.Services.AddScoped<MediaEpisodeIdentityService>();
builder.Services.AddScoped<ITrackMetadataSearchProvider, SpotifySearchProvider>();
builder.Services.AddScoped<ITrackMetadataSearchProvider, MusicBrainzSearchProvider>();
builder.Services.AddScoped<TrackMatchingService>();
builder.Services.AddScoped<SongGroupingSuggestionService>();
builder.Services.AddScoped<TrackMatchQueue>();
builder.Services.AddSingleton<MusicSyncThrottleService>();
builder.Services.AddScoped<MusicSyncJobProcessor>();
builder.Services.AddHostedService<MusicSyncJobWorker>();
builder.Services.AddHostedService<TrackMatchingWorker>();
builder.Services.AddHostedService<MediaProviderOperationWorker>();
builder.Services.AddHostedService<MediaObservationRetentionWorker>();
builder.Services.AddHostedService<MediaLibraryImportWorker>();
builder.Services.AddHostedService<MediaRelationGraphRefreshWorker>();
builder.Services.AddHostedService<AnimeScheduleAvailabilityRefreshWorker>();
builder.Services.AddScoped<ExtensionAuthorizationCodeStore>();
builder.Services.AddScoped<ExtensionAuthService>();
builder.Services.AddSingleton<IAvatarStore, S3AvatarStore>();

builder.Services.AddIdentityApiEndpoints<User>(c =>
{
    // Password settings - relaxed for development, should be strengthened in production
    c.Password.RequireDigit = false;
    c.Password.RequireLowercase = false;
    c.Password.RequireUppercase = false;
    c.Password.RequireNonAlphanumeric = false;
    c.Password.RequiredLength = 6;
    c.Password.RequiredUniqueChars = 1;

    // User settings
    c.User.RequireUniqueEmail = true;

    // Sign in settings
    c.SignIn.RequireConfirmedEmail = false;
    c.SignIn.RequireConfirmedAccount = false;
}).AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "CantaroApi";
        options.DefaultAuthenticateScheme = "CantaroApi";
        options.DefaultChallengeScheme = "CantaroApi";
    })
    .AddPolicyScheme("CantaroApi", "Cantaro API authentication", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            var hasSseAccessToken = context.Request.Path.StartsWithSegments("/api/media/providers")
                && context.Request.Path.Value?.EndsWith("/import/events", StringComparison.OrdinalIgnoreCase) == true
                && context.Request.Query.ContainsKey("access_token");

            return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || hasSseAccessToken
                ? JwtBearerDefaults.AuthenticationScheme
                : IdentityConstants.ApplicationScheme;
        };
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = extensionAuthOptions.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = extensionAuthOptions.JwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(extensionJwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api/media/providers")
                    && context.Request.Path.Value?.EndsWith("/import/events", StringComparison.OrdinalIgnoreCase) == true
                    && context.Request.Query.TryGetValue("access_token", out var accessToken))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.ConfigureApplicationCookie(options =>
{
    // Makes the cookie last 14 days
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;

    // options.LoginPath = "/api/identity/login";
    // options.LogoutPath = "/api/identity/logout";
    // options.SessionStore = 
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    if (builder.Environment.IsProduction())
    {
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    }

    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

// Add controllers
builder.Services.AddControllers();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add CORS for frontend communication
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var frontendOptions = builder.Configuration
            .GetSection(FrontendUrlOptions.SectionName)
            .Get<FrontendUrlOptions>() ?? new FrontendUrlOptions();
        var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frontendUrl = builder.Configuration["services:web:http:0"]
            ?? builder.Configuration["services:web:https:0"]
            ?? builder.Configuration["services:web:0"]
            ?? builder.Configuration[$"{FrontendUrlOptions.SectionName}:BaseUrl"];

        if (!string.IsNullOrWhiteSpace(frontendUrl))
        {
            allowedOrigins.Add(frontendUrl.TrimEnd('/'));
        }

        foreach (var trustedOrigin in frontendOptions.TrustedOrigins)
        {
            if (!string.IsNullOrWhiteSpace(trustedOrigin))
            {
                allowedOrigins.Add(trustedOrigin.TrimEnd('/'));
            }
        }

        if (allowedOrigins.Count > 0)
        {
            policy.WithOrigins(allowedOrigins.ToArray())
                .SetIsOriginAllowed(origin => IsTrustedFrontendOrigin(origin, frontendOptions))
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
            return;
        }

        if (!builder.Environment.IsDevelopment())
        {
            Console.WriteLine("WARNING: No explicit frontend origin configured. Same-origin requests will still work, but cross-origin frontend access requires Aspire service references or Frontend:BaseUrl.");
        }

        policy.AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapDefaultEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseHttpsRedirection();
var hasWebRoot = Directory.Exists(app.Environment.WebRootPath);
if (hasWebRoot)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Prefix all Identity API endpoints with /api to hit the vite proxy
app.MapGroup("/api").MapIdentityApi<User>();
if (hasWebRoot)
{
    app.MapFallbackToFile("index.html");
}

if (app.Environment.IsDevelopment())
{
    await SeedDevUserAsync(app.Services, app.Environment, app.Configuration, app.Logger);
}

//     // Apply migrations on startup in development
// if (app.Environment.IsDevelopment())
// {
//     using var scope = app.Services.CreateScope();
//     var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
//     var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();
//     logger.LogWarning("Applying EF Core migrations automatically on startup (development mode). " +
//         "This is intended for local development only. " +
//         "If multiple instances start simultaneously or if migrations fail, the database may become inconsistent. " +
//         "See the developer setup guide for details.");
//     await dbContext.Database.MigrateAsync();
// }

app.Run();

static async Task SeedDevUserAsync(
    IServiceProvider serviceProvider,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    ILogger logger)
{
    if (!hostEnvironment.IsDevelopment())
    {
        return;
    }

    var enabled = configuration.GetValue("SeedUser:Enabled", false);
    if (!enabled)
    {
        return;
    }

    var email = configuration["SeedUser:Email"]?.Trim();
    var password = configuration["SeedUser:Password"];

    if (string.IsNullOrWhiteSpace(email))
    {
        throw new InvalidOperationException("SeedUser:Email must be configured when SeedUser:Enabled is true.");
    }

    if (string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException("SeedUser:Password must be configured when SeedUser:Enabled is true.");
    }

    using var scope = serviceProvider.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

    var user = await userManager.FindByEmailAsync(email);
    if (user is null)
    {
        user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, password);
        EnsureIdentitySuccess(createResult, $"Creating seeded test user '{email}'");
        logger.LogInformation("Seeded test user {Email}.", email);
        return;
    }

    var needsUserUpdate = false;
    if (!string.Equals(user.UserName, email, StringComparison.OrdinalIgnoreCase))
    {
        user.UserName = email;
        needsUserUpdate = true;
    }

    if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
    {
        user.Email = email;
        needsUserUpdate = true;
    }

    if (!user.EmailConfirmed)
    {
        user.EmailConfirmed = true;
        needsUserUpdate = true;
    }

    if (needsUserUpdate)
    {
        var updateResult = await userManager.UpdateAsync(user);
        EnsureIdentitySuccess(updateResult, $"Updating seeded test user '{email}'");
    }

    if (await userManager.CheckPasswordAsync(user, password))
    {
        logger.LogInformation("Seeded test user {Email} already exists.", email);
        return;
    }

    IdentityResult passwordResult;
    if (await userManager.HasPasswordAsync(user))
    {
        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        passwordResult = await userManager.ResetPasswordAsync(user, resetToken, password);
    }
    else
    {
        passwordResult = await userManager.AddPasswordAsync(user, password);
    }

    EnsureIdentitySuccess(passwordResult, $"Setting password for seeded test user '{email}'");
    logger.LogInformation("Reset seeded test user password for {Email}.", email);
}

static bool IsTrustedFrontendOrigin(string origin, FrontendUrlOptions options)
{
    if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (options.TrustLoopbackOrigins && uri.IsLoopback)
    {
        return true;
    }

    foreach (var trustedOrigin in options.TrustedOrigins)
    {
        if (!Uri.TryCreate(trustedOrigin, UriKind.Absolute, out var trustedUri))
        {
            continue;
        }

        if (Uri.Compare(uri, trustedUri, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
        {
            return true;
        }
    }

    foreach (var suffix in options.TrustedHostSuffixes)
    {
        var normalizedSuffix = suffix.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSuffix)
            && uri.Host.EndsWith(normalizedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static void EnsureIdentitySuccess(IdentityResult result, string operation)
{
    if (result.Succeeded)
    {
        return;
    }

    throw new InvalidOperationException(
        $"{operation} failed: {string.Join("; ", result.Errors.Select(error => error.Description))}");
}
