using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var extensionAuthOptions = builder.Configuration
    .GetSection(ExtensionAuthOptions.SectionName)
    .Get<ExtensionAuthOptions>() ?? new ExtensionAuthOptions();
var extensionJwtSigningKey = ExtensionAuthSigningKeyResolver.ResolveSigningKey(extensionAuthOptions, builder.Environment);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add PostgreSQL database context via Aspire defaults.
builder.AddNpgsqlDbContext<ApplicationDbContext>(connectionName: "cantaro-db");

// Add Data Protection for token encryption
builder.Services.AddDataProtection();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<FrontendUrlOptions>(builder.Configuration.GetSection(FrontendUrlOptions.SectionName));
builder.Services
    .AddOptions<ExtensionAuthOptions>()
    .Bind(builder.Configuration.GetSection(ExtensionAuthOptions.SectionName))
    .ValidateDataAnnotations();
builder.Services
    .AddOptions<TrackMatchingOptions>()
    .Bind(builder.Configuration.GetSection(TrackMatchingOptions.SectionName))
    .ValidateDataAnnotations();
builder.Services
    .AddOptions<AniListOptions>()
    .Bind(builder.Configuration.GetSection(AniListOptions.SectionName));

// Register custom services
builder.Services.AddScoped<TokenEncryptionService>();
builder.Services.AddScoped<YouTubeService>();
builder.Services.AddScoped<YouTubePlaylistSyncService>();
builder.Services.AddScoped<IPlatformService, YouTubePlatformService>();
builder.Services.AddScoped<IPlatformRegistry, PlatformRegistry>();
builder.Services.AddScoped<IMediaProviderRegistry, MediaProviderRegistry>();
builder.Services.AddScoped<IFrontendUrlResolver, FrontendUrlResolver>();
builder.Services.AddHttpClient<IMusicBrainzQueryClient, MusicBrainzQueryClient>();
builder.Services.AddHttpClient<AniListApiClient>();
builder.Services.AddScoped<IMediaProvider, AniListMediaProvider>();
builder.Services.AddScoped<MediaLibraryImportService>();
builder.Services.AddScoped<MediaLibraryQueryService>();
builder.Services.AddScoped<MediaLibraryLinkService>();
builder.Services.AddScoped<MediaProviderOperationProcessor>();
builder.Services.AddScoped<MediaObservationMatchingService>();
builder.Services.AddScoped<MediaObservationProgressService>();
builder.Services.AddScoped<ITrackMetadataSearchProvider, MusicBrainzSearchProvider>();
builder.Services.AddScoped<TrackMatchingService>();
builder.Services.AddHostedService<MediaProviderOperationWorker>();
builder.Services.AddScoped<ExtensionAuthorizationCodeStore>();
builder.Services.AddScoped<ExtensionAuthService>();

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
            return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
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
        var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frontendUrl = builder.Configuration["services:web:http:0"]
            ?? builder.Configuration["services:web:https:0"]
            ?? builder.Configuration["services:web:0"]
            ?? builder.Configuration[$"{FrontendUrlOptions.SectionName}:BaseUrl"];

        if (!string.IsNullOrWhiteSpace(frontendUrl))
        {
            allowedOrigins.Add(frontendUrl.TrimEnd('/'));
        }

        if (allowedOrigins.Count > 0)
        {
            policy.WithOrigins(allowedOrigins.ToArray())
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Prefix all Identity API endpoints with /api to hit the vite proxy
app.MapGroup("/api").MapIdentityApi<User>();

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
