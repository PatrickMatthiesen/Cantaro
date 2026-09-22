using System.Net;
using System.Reflection;
using System.Text;
using Cantaro.Api.Controllers;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class LogoutTests
{
    [Fact]
    public async Task Logout_ClearsApplicationCookieAndInvalidatesProtectedRequests()
    {
        await using var fixture = await TestAppFixture.StartAsync();

        await fixture.SignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("/api/test/protected")).StatusCode);
        Assert.Single(fixture.Cookies.GetCookies(fixture.BaseAddress).Cast<Cookie>());

        using var logoutResponse = await fixture.Client.PostAsync(
            "/api/logout",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var protectedResponse = await fixture.Client.GetAsync("/api/test/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_RejectsFormPostsWithoutDeletingAnAuthenticatedCookie()
    {
        await using var fixture = await TestAppFixture.StartAsync();

        await fixture.SignInAsync();
        var cookieBefore = Assert.Single(fixture.Cookies.GetCookies(fixture.BaseAddress).Cast<Cookie>());

        using var response = await fixture.Client.PostAsync(
            "/api/logout",
            new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);

        var cookieAfter = Assert.Single(fixture.Cookies.GetCookies(fixture.BaseAddress).Cast<Cookie>());
        Assert.Equal(cookieBefore.Value, cookieAfter.Value);

        using var protectedResponse = await fixture.Client.GetAsync("/api/test/protected");
        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_IsIdempotentForAnAnonymousClient()
    {
        await using var fixture = await TestAppFixture.StartAsync();

        using var firstResponse = await fixture.Client.PostAsync(
            "/api/logout",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        using var secondResponse = await fixture.Client.PostAsync(
            "/api/logout",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondResponse.StatusCode);
    }

    private sealed class TestAppFixture : IAsyncDisposable
    {
        private TestAppFixture(
            WebApplication app,
            HttpClient client,
            CookieContainer cookies,
            Uri baseAddress)
        {
            App = app;
            Client = client;
            Cookies = cookies;
            BaseAddress = baseAddress;
        }

        public WebApplication App { get; }
        public HttpClient Client { get; }
        public CookieContainer Cookies { get; }
        public Uri BaseAddress { get; }

        public static async Task<TestAppFixture> StartAsync()
        {
            var databaseName = $"logout-tests-{Guid.NewGuid():N}";
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
            builder.Services.AddIdentityCore<User>()
                .AddSignInManager()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            builder.Services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                    options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
                })
                .AddIdentityCookies();
            builder.Services.Configure<CookieAuthenticationOptions>(
                IdentityConstants.ApplicationScheme,
                options =>
                {
                    options.Events.OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    };
                    options.Events.OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    };
                });
            builder.Services.AddAuthorization();
            builder.Services
                .AddControllers()
                .ConfigureApplicationPartManager(manager =>
                {
                    manager.ApplicationParts.Clear();
                    manager.ApplicationParts.Add(new AssemblyPart(typeof(SessionController).Assembly));
                    manager.FeatureProviders.Remove(
                        manager.FeatureProviders.OfType<ControllerFeatureProvider>().Single());
                    manager.FeatureProviders.Add(new SessionOnlyControllerFeatureProvider());
                });

            var app = builder.Build();
            var user = new User
            {
                UserName = "logout-test@example.com",
                Email = "logout-test@example.com",
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
                var result = await userManager.CreateAsync(user);
                Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
            }

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapPost(
                    "/api/test/sign-in",
                    async (SignInManager<User> signInManager) =>
                    {
                        await signInManager.SignInAsync(user, isPersistent: false);
                        return Results.NoContent();
                    });
            app.MapGet(
                    "/api/test/protected",
                    (HttpContext context) => Results.Ok(context.User.Identity?.Name))
                .RequireAuthorization();
            app.MapControllers();

            await app.StartAsync();
            var server = app.Services.GetRequiredService<IServer>();
            var address = new Uri(server.Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            var cookies = new CookieContainer();
            var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CookieContainer = cookies,
                UseCookies = true
            })
            {
                BaseAddress = address
            };

            return new TestAppFixture(app, client, cookies, address);
        }

        public async Task SignInAsync()
        {
            using var response = await Client.PostAsync("/api/test/sign-in", content: null);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    private sealed class SessionOnlyControllerFeatureProvider : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo)
        {
            return typeInfo.AsType() == typeof(SessionController);
        }
    }
}
