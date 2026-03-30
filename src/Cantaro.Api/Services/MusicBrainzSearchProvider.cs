using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public class MusicBrainzSearchProvider : ITrackMetadataSearchProvider
{
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    private readonly IMusicBrainzQueryClient _musicBrainzQueryClient;
    private readonly ILogger<MusicBrainzSearchProvider> _logger;

    public MusicBrainzSearchProvider(
        IMusicBrainzQueryClient musicBrainzQueryClient,
        ILogger<MusicBrainzSearchProvider> logger)
    {
        _musicBrainzQueryClient = musicBrainzQueryClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TrackMatchSearchCandidate>> SearchAsync(TrackObservation observation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(observation.Title))
        {
            return [];
        }

        var parsedMetadata = TrackMetadataParser.Parse(observation.Title, observation.Artist);
        var queryPlans = BuildSearchPlans(observation, parsedMetadata);
        var candidates = new Dictionary<string, TrackMatchSearchCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var queryPlan in queryPlans)
        {
            await RespectRateLimitAsync(cancellationToken);

            var matches = await _musicBrainzQueryClient.FindRecordingsAsync(queryPlan.Query, limit: 5, cancellationToken);
            foreach (var match in matches)
            {
                if (candidates.ContainsKey(match.ExternalId))
                {
                    continue;
                }

                candidates.Add(match.ExternalId, new TrackMatchSearchCandidate
                {
                    CandidateSource = "musicbrainz",
                    ExternalId = match.ExternalId,
                    Title = match.Title,
                    Artist = match.Artist,
                    MbidRecording = match.MbidRecording,
                    Isrc = match.Isrc,
                    DurationSeconds = match.DurationSeconds,
                    Explanation = $"Suggested by MusicBrainz {queryPlan.Description} search (search score: {match.SearchScore}/100).",
                    RawMetadata = match.RawMetadata
                });
            }

            if (candidates.Count >= 5)
            {
                break;
            }
        }

        _logger.LogDebug("MusicBrainz returned {Count} candidate(s) for observation {ObservationId}", candidates.Count, observation.Id);

        return candidates.Values.Take(5).ToList();
    }

    private static async Task RespectRateLimitAsync(CancellationToken cancellationToken)
    {
        await RequestGate.WaitAsync(cancellationToken);
        try
        {
            var earliestNextRequest = _lastRequestAt.AddSeconds(1);
            var delay = earliestNextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            _lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private static List<MusicBrainzSearchPlan> BuildSearchPlans(TrackObservation observation, ParsedTrackMetadata parsedMetadata)
    {
        var plans = new List<MusicBrainzSearchPlan>();
        AddPlan(plans, parsedMetadata.SearchTitle, parsedMetadata.SearchArtist, "title-and-artist");
        AddPlan(plans, parsedMetadata.DisplayTitle, parsedMetadata.DisplayArtist, "cleaned title-and-artist");
        AddPlan(plans, parsedMetadata.SearchTitle, artist: null, "title-only");
        AddPlan(plans, parsedMetadata.DisplayTitle, artist: null, "cleaned title-only");
        AddPlan(plans, observation.Title, artist: null, "raw title-only");

        return plans
            .GroupBy(plan => plan.Query, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddPlan(List<MusicBrainzSearchPlan> plans, string? title, string? artist, string description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var queryParts = new List<string>
        {
            $"recording:\"{EscapeSearchTerm(title)}\""
        };

        if (!string.IsNullOrWhiteSpace(artist))
        {
            queryParts.Add($"artist:\"{EscapeSearchTerm(artist)}\"");
        }

        plans.Add(new MusicBrainzSearchPlan(string.Join(" AND ", queryParts), description));
    }

    private static string EscapeSearchTerm(string value) => value.Replace("\"", string.Empty, StringComparison.Ordinal);

    private sealed record MusicBrainzSearchPlan(string Query, string Description);
}
