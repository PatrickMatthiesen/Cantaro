namespace Cantaro.Api.Services;

public interface IAioStreamsAnimeEnricher
{
    Task EnrichAsync(MediaProviderTitleDetails details, CancellationToken cancellationToken);
}
