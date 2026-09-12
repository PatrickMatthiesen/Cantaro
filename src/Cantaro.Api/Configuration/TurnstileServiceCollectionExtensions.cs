using Cantaro.Api.Services;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Configuration;

public static class TurnstileServiceCollectionExtensions
{
    public static IServiceCollection AddTurnstile(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TurnstileOptions>()
            .Bind(configuration.GetSection(TurnstileOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TurnstileOptions>, TurnstileOptionsValidator>();

        services.AddHttpClient("turnstile", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<ITurnstileValidator, TurnstileValidator>();
        return services;
    }
}
