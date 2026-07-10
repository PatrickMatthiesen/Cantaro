namespace Cantaro.Api.Services.Lyrics;

public interface ILyricsProvider
{
    Task<LyricsResult> GetLyricsAsync(LyricsLookup lookup, CancellationToken cancellationToken);
}
