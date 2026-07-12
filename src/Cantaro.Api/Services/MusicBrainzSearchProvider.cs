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
    private readonly Func<CancellationToken, Task> _respectRateLimitAsync;

    public MusicBrainzSearchProvider(
        IMusicBrainzQueryClient musicBrainzQueryClient,
        ILogger<MusicBrainzSearchProvider> logger)
        : this(musicBrainzQueryClient, logger, Options.Create(new TrackMatchingOptions()))
    {
    }

    public MusicBrainzSearchProvider(
        IMusicBrainzQueryClient musicBrainzQueryClient,
        ILogger<MusicBrainzSearchProvider> logger,
        IOptions<TrackMatchingOptions> options,
        Func<CancellationToken, Task>? respectRateLimitAsync = null)
    {
        _musicBrainzQueryClient = musicBrainzQueryClient;
        _logger = logger;
        _options = options.Value;
        _respectRateLimitAsync = respectRateLimitAsync ?? RespectRateLimitAsync;
    }

    public async Task<IReadOnlyList<TrackMatchSearchCandidate>> SearchAsync(TrackObservation observation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(observation.Title))
        {
            return [];
        }

        var parsedMetadata = TrackMetadataParser.Parse(observation.Title, observation.Artist);
        var queryPlans = BuildSearchPlans(parsedMetadata)
            .Take(_options.MusicBrainzMaxRequestsPerSearch)
            .ToList();
        var candidates = new Dictionary<string, RankedSearchCandidate>(StringComparer.OrdinalIgnoreCase);

        for (var queryIndex = 0; queryIndex < queryPlans.Count; queryIndex++)
        {
            var queryPlan = queryPlans[queryIndex];
            await _respectRateLimitAsync(cancellationToken);

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
                        ArtistCredits = match.ArtistCredits,
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

            if (matches.Any(match => IsLocallyCredibleExactMatch(observation, parsedMetadata, match)))
            {
                _logger.LogDebug(
                    "Stopping MusicBrainz search after credible exact candidate from {Description} query for observation {ObservationId}",
                    queryPlan.Description,
                    observation.Id);
                break;
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

    private List<MusicBrainzSearchPlan> BuildSearchPlans(ParsedTrackMetadata parsedMetadata)
    {
        var plans = new List<MusicBrainzSearchPlan>();
        IReadOnlyList<string> individualArtistFallbacks = [];

        // The original, complete credit is the most selective query and should always run first.
        var fullCredit = parsedMetadata.DisplayArtist ?? parsedMetadata.SearchArtist;
        AddPlan(plans, parsedMetadata.SearchTitle, fullCredit, "strict title-and-full-credit");

        var collaborators = SplitCollaborators(fullCredit ?? string.Empty);
        if (collaborators.Count > 1)
        {
            AddArtistNamesPlan(plans, parsedMetadata.SearchTitle, collaborators, "title-and-each-artist-credit");
            AddCompactTitleSpacingPlan(plans, parsedMetadata.SearchTitle, collaborators);

            // Individual artist fallbacks are bounded. They help with incomplete MusicBrainz
            // credits without spending requests on separator variants that repeat the same miss.
            individualArtistFallbacks = collaborators
                .Take(_options.MusicBrainzCollaboratorVariantLimit)
                .ToList();
        }
        else
        {
            AddCompactTitleSpacingPlan(plans, parsedMetadata.SearchTitle, collaborators);
        }

        AddPlan(plans, parsedMetadata.SearchTitle, artist: null, "title-only");
        AddPlan(plans, parsedMetadata.DisplayTitle, artist: null, "cleaned title-only");
        AddPlans(
            plans,
            parsedMetadata.SearchTitle,
            individualArtistFallbacks,
            "title-and-individual-artist");

        return plans
            .GroupBy(plan => plan.Query, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddPlans(List<MusicBrainzSearchPlan> plans, string? title, IReadOnlyList<string> artists, string description)
    {
        if (artists.Count == 0)
        {
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

    private static void AddArtistNamesPlan(
        List<MusicBrainzSearchPlan> plans,
        string? title,
        IReadOnlyList<string> artists,
        string description)
    {
        if (string.IsNullOrWhiteSpace(title) || artists.Count == 0)
        {
            return;
        }

        var queryParts = new List<string> { $"recording:\"{EscapeSearchTerm(title)}\"" };
        queryParts.AddRange(artists.Select(artist => $"artistname:\"{EscapeSearchTerm(artist)}\""));
        plans.Add(new MusicBrainzSearchPlan(string.Join(" AND ", queryParts), description));
    }

    private static void AddCompactTitleSpacingPlan(
        List<MusicBrainzSearchPlan> plans,
        string? title,
        IReadOnlyList<string> artists)
    {
        if (string.IsNullOrWhiteSpace(title)
            || title.Length < 6
            || title.Any(char.IsWhiteSpace)
            || artists.Count == 0)
        {
            return;
        }

        var titleVariants = Enumerable.Range(2, title.Length - 3)
            .Select(index => $"{title[..index]} {title[index..]}")
            .Select(variant => $"\"{EscapeSearchTerm(variant)}\"")
            .ToList();
        var queryParts = new List<string> { $"recording:({string.Join(" OR ", titleVariants)})" };
        queryParts.AddRange(artists.Select(artist => $"artistname:\"{EscapeSearchTerm(artist)}\""));
        plans.Add(new MusicBrainzSearchPlan(string.Join(" AND ", queryParts), "compact-title-spacing-and-artist-credit"));
    }

    private bool IsLocallyCredibleExactMatch(
        TrackObservation observation,
        ParsedTrackMetadata parsedObservation,
        MusicBrainzRecordingMatch match)
    {
        var parsedCandidate = TrackMetadataParser.Parse(match.Title, match.Artist);
        var exactTitle = HaveEqualNormalizedText(parsedObservation.SearchTitle, parsedCandidate.SearchTitle);
        var exactArtistCredit = TrackMetadataParser.HaveEquivalentArtistCredits(
            parsedObservation,
            parsedCandidate,
            match.ArtistCredits);
        var semanticsAgree = !TrackMatchScorer.HaveDifferentMarkers(parsedObservation.VersionMarkers, parsedCandidate.VersionMarkers)
            && !TrackMatchScorer.HaveDifferentMarkers(parsedObservation.PlaybackModifiers, parsedCandidate.PlaybackModifiers);
        var durationIsConsistent = !observation.DurationSeconds.HasValue
            || !match.DurationSeconds.HasValue
            || Math.Abs(observation.DurationSeconds.Value - match.DurationSeconds.Value) <= _options.AutoMatchDurationToleranceSeconds;

        // SearchScore is deliberately excluded: it is a remote ranking signal, not enough evidence
        // to stop the local fallback search safely.
        return exactTitle && exactArtistCredit && semanticsAgree && durationIsConsistent;
    }

    private static bool HaveEqualNormalizedText(string? left, string? right)
    {
        return TrackTextNormalizer.AreEquivalentTitles(left, right);
    }

    private static List<string> SplitCollaborators(string artist)
    {
        var segments = System.Text.RegularExpressions.Regex
            .Split(artist, @"\s+[x×]\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToList();
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
