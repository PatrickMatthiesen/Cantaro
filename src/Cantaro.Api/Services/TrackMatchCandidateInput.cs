using Cantaro.Api.Models;
using Cantaro.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Cantaro.Api.Services;

internal static class TrackMatchCandidateInput
{
    public static TrackMatchSearchCandidate? Read(TrackResolutionCandidate candidate, DateTimeOffset now)
    {
        if (candidate.ProviderCatalogItemId is { } id)
        {
            var item = candidate.ProviderCatalogItem;
            if (item is null || item.Id != id || item.Revision != candidate.ProviderCatalogRevision
                || item.SourceType != candidate.CandidateSource || item.ExternalId != candidate.ExternalId
                || ProviderCatalogPolicy.ReadFacts(item, now) is not { } facts) return null;
            var input = ObservationInput.FromFacts(item.SourceType, item.ExternalId, facts);
            return new TrackMatchSearchCandidate
            {
                CandidateSource = item.SourceType, ExternalId = item.ExternalId, Title = input.Title,
                Artist = input.Artist, DurationSeconds = input.DurationSeconds, Isrc = facts.Isrc,
                MbidRecording = facts.MusicBrainzRecordingId, ArtistIdentities = facts.ArtistCredits,
                ArtistCredits = facts.ArtistCredits.Select(c => c.Name).ToArray(),
                ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision,
                RawMetadata = System.Text.Json.JsonSerializer.Serialize(input.Metadata)
            };
        }
        return string.IsNullOrWhiteSpace(candidate.Title) ? null : new TrackMatchSearchCandidate
        {
            CandidateSource = candidate.CandidateSource, ExternalId = candidate.ExternalId,
            Title = candidate.Title, Artist = candidate.Artist, DurationSeconds = candidate.DurationSeconds,
            MbidRecording = candidate.MbidRecording, Isrc = candidate.Isrc
        };
    }

    public static async Task<TrackMatchSearchCandidate> ReloadAsync(ApplicationDbContext db,
        TrackResolutionCandidate candidate, CancellationToken ct)
    {
        if (candidate.ProviderCatalogItemId is { } id)
        {
            var item = await db.ProviderCatalogItems.SingleOrDefaultAsync(i => i.Id == id, ct);
            if (item is not null) await db.Entry(item).ReloadAsync(ct);
            candidate.ProviderCatalogItem = item;
        }
        return Read(candidate, DateTimeOffset.UtcNow)
            ?? throw new InvalidOperationException("Candidate catalog facts changed or expired. Retry matching before accepting it.");
    }
}
