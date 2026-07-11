using Cantaro.Api.Configuration;
using Cantaro.Api.Models;
using Microsoft.Extensions.Options;

namespace Cantaro.Api.Services;

public class MusicBrainzSearchProvider : ITrackMetadataSearchProvider
{
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    private readonly IMusicBrainzQueryClient _musicBrainzQueryClient;
    private readonly ILogger<MusicBrainzSearchProvider> _logger;
    private readonly TrackMatchingOptions _options;

    public MusicBrainzSearchProvider(
        IMusicBrainzQueryClient musicBrainzQueryClient,
        ILogger<MusicBrainzSearchProvider> logger)
        : this(musicBrainzQueryClient, logger, Options.Create(new TrackMatchingOptions()))
    {
    }

    public MusicBrainzSearchProvider(
        IMusicBrainzQueryClient musicBrainzQueryClient,
        ILogger<MusicBrainzSearchProvider> logger,
        IOptions<TrackMatchingOptions> options)
    {
        _musicBrainzQueryClient = musicBrainzQueryClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<TrackMatchSearchCandidate>> SearchAsync(TrackObservation observation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(observation.Title))
        {
            return [];
        }

        var parsedMetadata = TrackMetadataParser.Parse(observation.Title, observation.Artist);
        var queryPlans = BuildSearchPlans(observation, parsedMetadata);
        var candidates = new Dictionary<string, RankedSearchCandidate>(StringComparer.OrdinalIgnoreCase);

        for (var queryIndex = 0; queryIndex < queryPlans.Count; queryIndex++)
        {
            var queryPlan = queryPlans[queryIndex];
            await RespectRateLimitAsync(cancellationToken);

            var matches = await _musicBrainzQueryClient.FindRecordingsAsync(queryPlan.Query, limit: _options.MusicBrainzPerQueryResultLimit, cancellationToken);
            for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
            {
                var match = matches[matchIndex];
                var candidate = new RankedSearchCandidate(
                    new TrackMatchSearchCandidate
                    {
                        CandidateSource = "musicbrainz",
                        ExternalId = match.ExternalId,
                        Title = match.Title,
                        Artist = match.Artist,
                        ArtistMusicBrainzId = match.ArtistMusicBrainzId,
                        ArtistSortName = match.ArtistSortName,
                        MbidRecording = match.MbidRecording,
                        Isrc = match.Isrc,
                        DurationSeconds = match.DurationSeconds,
                        Explanation = $"Suggested by MusicBrainz {queryPlan.Description} search (search score: {match.SearchScore}/100).",
                        RawMetadata = match.RawMetadata
                    },
                    match.SearchScore,
                    queryIndex,
                    matchIndex);

                if (candidates.TryGetValue(match.ExternalId, out var existingCandidate))
                {
                    if (candidate.ShouldReplace(existingCandidate))
                    {
                        candidates[match.ExternalId] = candidate;
                    }

                    continue;
                }

                candidates.Add(match.ExternalId, candidate);
            }
        }

        var orderedCandidates = candidates.Values
            .OrderByDescending(candidate => candidate.SearchScore)
            .ThenBy(candidate => candidate.QueryIndex)
            .ThenBy(candidate => candidate.MatchIndex)
            .ThenBy(candidate => candidate.Candidate.Title, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Candidate.Artist, StringComparer.Ordinal)
            .Select(candidate => candidate.Candidate)
            .Take(_options.MusicBrainzMaxReturnedCandidates)
            .ToList();

        _logger.LogDebug("MusicBrainz returned {Count} candidate(s) for observation {ObservationId}", orderedCandidates.Count, observation.Id);

        return orderedCandidates;
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
        AddPlans(plans, parsedMetadata.SearchTitle, BuildArtistVariants(parsedMetadata.SearchArtist), "title-and-artist");
        AddPlans(plans, parsedMetadata.DisplayTitle, BuildArtistVariants(parsedMetadata.DisplayArtist), "cleaned title-and-artist");
        AddPlan(plans, parsedMetadata.SearchTitle, artist: null, "title-only");
        AddPlan(plans, parsedMetadata.DisplayTitle, artist: null, "cleaned title-only");
        AddPlan(plans, observation.Title, artist: null, "raw title-only");

        return plans
            .GroupBy(plan => plan.Query, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddPlans(List<MusicBrainzSearchPlan> plans, string? title, IReadOnlyList<string> artists, string description)
    {
        if (artists.Count == 0)
        {
            AddPlan(plans, title, artist: null, description);
            return;
        }

        foreach (var artist in artists)
        {
            AddPlan(plans, title, artist, description);
        }
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

    private static IReadOnlyList<string> BuildArtistVariants(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return [];
        }

        var normalizedArtist = NormalizeArtistWhitespace(artist);
        var collaborators = SplitCollaborators(normalizedArtist);
        var variants = new List<string> { normalizedArtist };

        if (collaborators.Count > 1)
        {
            variants.Add(string.Join(" & ", collaborators));
            variants.Add(string.Join(", ", collaborators));
            variants.AddRange(collaborators);
        }

        return variants
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> SplitCollaborators(string artist)
    {
        var separators = new[]
        {
            " featuring ",
            " feat. ",
            " feat ",
            " ft. ",
            " ft ",
            " with ",
            " & ",
            " and ",
            ";",
            "/",
            ","
        };

        var segments = new List<string> { artist };
        foreach (var separator in separators)
        {
            segments = segments
                .SelectMany(segment => segment.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .ToList();
        }

        return segments
            .Select(NormalizeArtistWhitespace)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeArtistWhitespace(string artist) => string.Join(" ", artist.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string EscapeSearchTerm(string value) => value.Replace("\"", string.Empty, StringComparison.Ordinal);

    private sealed record MusicBrainzSearchPlan(string Query, string Description);

    private sealed record RankedSearchCandidate(
        TrackMatchSearchCandidate Candidate,
        int SearchScore,
        int QueryIndex,
        int MatchIndex)
    {
        public bool ShouldReplace(RankedSearchCandidate existing)
        {
            if (SearchScore != existing.SearchScore)
            {
                return SearchScore > existing.SearchScore;
            }

            if (QueryIndex != existing.QueryIndex)
            {
                return QueryIndex < existing.QueryIndex;
            }

            return MatchIndex < existing.MatchIndex;
        }
    }
}
