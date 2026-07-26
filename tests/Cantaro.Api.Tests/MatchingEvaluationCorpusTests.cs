using System.Text.Json;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cantaro.Api.Tests;

public sealed class MatchingEvaluationCorpusTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static TheoryData<MatchingEvaluationCase> Cases
    {
        get
        {
            var data = new TheoryData<MatchingEvaluationCase>();
            foreach (var testCase in LoadCorpus())
            {
                data.Add(testCase);
            }

            return data;
        }
    }

    [Fact]
    public void Corpus_HasTraceableUniqueCases()
    {
        var cases = LoadCorpus();

        Assert.NotEmpty(cases);
        Assert.Equal(cases.Count, cases.Select(testCase => testCase.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(cases.Count, cases.Select(testCase => testCase.OriginalUrl).Distinct(StringComparer.Ordinal).Count());

        foreach (var testCase in cases)
        {
            AssertHttpsUrl(testCase.OriginalUrl);
            Assert.Equal("www.youtube.com", new Uri(testCase.OriginalUrl).Host);
            Assert.False(string.IsNullOrWhiteSpace(testCase.Observation.Title));
            Assert.Contains(
                testCase.Expected.Outcome,
                new[] { "exact_recording", "manual_review", "no_match" },
                StringComparer.Ordinal);

            foreach (var candidate in testCase.Candidates)
            {
                AssertHttpsUrl(candidate.SourceUrl);
                Assert.False(string.IsNullOrWhiteSpace(candidate.Source));
                Assert.False(string.IsNullOrWhiteSpace(candidate.ExternalId));
                Assert.False(string.IsNullOrWhiteSpace(candidate.Title));
            }

            if (testCase.Expected.Outcome == "exact_recording")
            {
                Assert.False(string.IsNullOrWhiteSpace(testCase.Expected.AcceptedExternalId));
                Assert.Contains(
                    testCase.Candidates,
                    candidate => candidate.Trusted
                        && candidate.ExternalId == testCase.Expected.AcceptedExternalId);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Corpus_ParsesExpectedObservationMetadata(MatchingEvaluationCase testCase)
    {
        var parsed = TrackMetadataParser.Parse(
            testCase.Observation.Title,
            testCase.Observation.Artist);

        if (testCase.Expected.NormalizedTitle is not null)
        {
            Assert.Equal(testCase.Expected.NormalizedTitle, parsed.SearchTitle);
        }

        if (testCase.Expected.ArtistCredits is not null)
        {
            Assert.Equal(
                testCase.Expected.ArtistCredits.OrderBy(value => value, StringComparer.Ordinal),
                parsed.ArtistCredits);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Corpus_ProducesExpectedSafetyDecision(MatchingEvaluationCase testCase)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var observation = new TrackObservation
        {
            Id = Guid.NewGuid(),
            SourceType = "youtube",
            ExternalId = new Uri(testCase.OriginalUrl).Query.TrimStart('?'),
            Title = testCase.Observation.Title,
            Artist = testCase.Observation.Artist,
            DurationSeconds = testCase.Observation.DurationSeconds,
            MatchStatus = TrackMatchingStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        dbContext.TrackObservations.Add(observation);
        await dbContext.SaveChangesAsync();

        var provider = new CorpusMetadataSearchProvider(testCase.Candidates);
        var result = await new TrackMatchingService(
                dbContext,
                [provider],
                NullLogger<TrackMatchingService>.Instance)
            .ProcessObservationAsync(observation.Id, CancellationToken.None);

        switch (testCase.Expected.Outcome)
        {
            case "exact_recording":
                Assert.Equal(TrackMatchingStatuses.Matched, result.MatchStatus);
                Assert.NotNull(result.AcceptedCandidateId);
                var accepted = await dbContext.TrackResolutionCandidates
                    .SingleAsync(candidate => candidate.IsAccepted);
                Assert.Equal(testCase.Expected.AcceptedExternalId, accepted.ExternalId);
                break;

            case "manual_review":
                Assert.NotEqual(TrackMatchingStatuses.Matched, result.MatchStatus);
                Assert.Null(result.AcceptedCandidateId);
                Assert.Contains(
                    await dbContext.TrackResolutionCandidates.ToListAsync(),
                    candidate => testCase.Candidates.Any(
                        corpusCandidate => corpusCandidate.Trusted
                            && corpusCandidate.ExternalId == candidate.ExternalId));
                break;

            case "no_match":
                Assert.Equal(TrackMatchingStatuses.NoMatch, result.MatchStatus);
                Assert.Null(result.AcceptedCandidateId);
                Assert.Empty(await dbContext.TrackResolutionCandidates.ToListAsync());
                break;

            default:
                throw new InvalidOperationException($"Unsupported expected outcome '{testCase.Expected.Outcome}'.");
        }
    }

    private static IReadOnlyList<MatchingEvaluationCase> LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "matching-evaluation.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<MatchingEvaluationCase>>(stream, SerializerOptions)
            ?? throw new InvalidOperationException($"Could not deserialize matching evaluation corpus at '{path}'.");
    }

    private static void AssertHttpsUrl(string value)
    {
        Assert.True(
            Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
            $"Expected an absolute HTTPS URL, but found '{value}'.");
    }

    private sealed class CorpusMetadataSearchProvider(IReadOnlyList<MatchingEvaluationCandidate> candidates)
        : ITrackMetadataSearchProvider
    {
        public Task<IReadOnlyList<TrackMatchSearchCandidate>> SearchAsync(
            TrackObservation observation,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TrackMatchSearchCandidate> results = candidates
                .Select(candidate => new TrackMatchSearchCandidate
                {
                    CandidateSource = candidate.Source,
                    ExternalId = candidate.ExternalId,
                    Title = candidate.Title,
                    Artist = candidate.Artist,
                    ArtistCredits = candidate.ArtistCredits,
                    DurationSeconds = candidate.DurationSeconds,
                    Isrc = candidate.Isrc,
                    Explanation = $"Verified corpus source: {candidate.SourceUrl}",
                    RawMetadata = JsonSerializer.Serialize(new
                    {
                        sourceUrl = candidate.SourceUrl,
                        candidate.Trusted
                    })
                })
                .ToArray();

            return Task.FromResult(results);
        }
    }
}

public sealed class MatchingEvaluationCase
{
    public required string Id { get; init; }
    public required string OriginalUrl { get; init; }
    public required MatchingEvaluationObservation Observation { get; init; }
    public IReadOnlyList<MatchingEvaluationCandidate> Candidates { get; init; } = [];
    public required MatchingEvaluationExpected Expected { get; init; }

    public override string ToString() => Id;
}

public sealed class MatchingEvaluationObservation
{
    public required string Title { get; init; }
    public string? Artist { get; init; }
    public int DurationSeconds { get; init; }
}

public sealed class MatchingEvaluationCandidate
{
    public required string Source { get; init; }
    public required string ExternalId { get; init; }
    public required string SourceUrl { get; init; }
    public required string Title { get; init; }
    public string? Artist { get; init; }
    public IReadOnlyList<string> ArtistCredits { get; init; } = [];
    public int? DurationSeconds { get; init; }
    public string? Isrc { get; init; }
    public bool Trusted { get; init; }
}

public sealed class MatchingEvaluationExpected
{
    public required string Outcome { get; init; }
    public string? NormalizedTitle { get; init; }
    public IReadOnlyList<string>? ArtistCredits { get; init; }
    public string? AcceptedExternalId { get; init; }
}
