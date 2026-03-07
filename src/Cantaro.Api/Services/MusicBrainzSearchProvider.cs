using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

public class MusicBrainzSearchProvider : ITrackMetadataSearchProvider
{
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MusicBrainzSearchProvider> _logger;

    public MusicBrainzSearchProvider(HttpClient httpClient, ILogger<MusicBrainzSearchProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("https://musicbrainz.org/");
        }

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Cantaro/0.1.0 ( https://github.com/PatrickMatthiesen/Cantaro )");
        }

        if (!_httpClient.DefaultRequestHeaders.Accept.Any())
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    public async Task<IReadOnlyList<TrackMatchSearchCandidate>> SearchAsync(TrackObservation observation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(observation.Title))
        {
            return [];
        }

        var queryParts = new List<string>
        {
            $"recording:\"{EscapeSearchTerm(observation.Title)}\""
        };

        if (!string.IsNullOrWhiteSpace(observation.Artist))
        {
            queryParts.Add($"artist:\"{EscapeSearchTerm(observation.Artist)}\"");
        }

        var query = string.Join(" AND ", queryParts);
        var requestUri = $"ws/2/recording?query={Uri.EscapeDataString(query)}&limit=5&fmt=json";

        await RespectRateLimitAsync(cancellationToken);

        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<MusicBrainzSearchResponse>(
            responseStream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken);

        if (payload?.Recordings == null || payload.Recordings.Count == 0)
        {
            return [];
        }

        var candidates = payload.Recordings
            .Where(recording => !string.IsNullOrWhiteSpace(recording.Id) && !string.IsNullOrWhiteSpace(recording.Title))
            .Select(recording =>
            {
                var artist = string.Join(", ", recording.ArtistCredit?
                    .Select(credit => credit.Artist?.Name ?? credit.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)) ?? []);

                return new TrackMatchSearchCandidate
                {
                    CandidateSource = "musicbrainz",
                    ExternalId = recording.Id!,
                    Title = recording.Title!,
                    Artist = string.IsNullOrWhiteSpace(artist) ? null : artist,
                    MbidRecording = recording.Id,
                    Isrc = recording.Isrcs?.FirstOrDefault(),
                    DurationSeconds = recording.LengthMilliseconds.HasValue
                        ? (int)Math.Round(recording.LengthMilliseconds.Value / 1000d, MidpointRounding.AwayFromZero)
                        : null,
                    Explanation = "Suggested by MusicBrainz recording search.",
                    RawMetadata = JsonSerializer.Serialize(recording)
                };
            })
            .ToList();

        _logger.LogDebug("MusicBrainz returned {Count} candidate(s) for observation {ObservationId}", candidates.Count, observation.Id);

        return candidates;
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

    private static string EscapeSearchTerm(string value) => value.Replace("\"", string.Empty, StringComparison.Ordinal);

    private sealed class MusicBrainzSearchResponse
    {
        [JsonPropertyName("recordings")]
        public List<MusicBrainzRecording>? Recordings { get; set; }
    }

    private sealed class MusicBrainzRecording
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("length")]
        public int? LengthMilliseconds { get; set; }

        [JsonPropertyName("artist-credit")]
        public List<MusicBrainzArtistCredit>? ArtistCredit { get; set; }

        [JsonPropertyName("isrcs")]
        public List<string>? Isrcs { get; set; }
    }

    private sealed class MusicBrainzArtistCredit
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("artist")]
        public MusicBrainzArtist? Artist { get; set; }
    }

    private sealed class MusicBrainzArtist
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
