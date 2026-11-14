using System.Text.Json;
using Cantaro.Api.Models;
using Cantaro.Api.Services.Spotify;

namespace Cantaro.Api.Services;

/// <summary>Creates trusted catalog records only from provider API responses observed here.</summary>
public sealed class ProviderCatalogAcquisition(
    ProviderCatalogStore store,
    SpotifyApiClient spotifyApi,
    SpotifyCatalogTokenProvider spotifyTokens,
    TimeProvider clock)
{
    public async Task<ProviderCatalogItem?> EnsureSpotifyAsync(string externalId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return null;
        var existing = await store.FindAsync("spotify", externalId, ct);
        if (ProviderCatalogPolicy.IsUsable(existing, clock.GetUtcNow())) return existing;
        if (!spotifyTokens.IsConfigured) return null;

        try
        {
            var token = await spotifyTokens.GetAccessTokenAsync(ct);
            if (string.IsNullOrWhiteSpace(token)) return null;
            var snapshot = await spotifyApi.GetTrackAsync(token, externalId, ct);
            if (snapshot is null || snapshot.Id != externalId)
            {
                await store.MarkWithdrawnAsync("spotify", externalId, ct);
                return null;
            }
            return await StoreSpotifyFactsAsync(snapshot, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (PlatformApiException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            await store.MarkWithdrawnAsync("spotify", externalId, ct);
            return null;
        }
        catch (Exception ex) when (ex is PlatformApiException or HttpRequestException or IOException or JsonException)
        {
            // A failed check does not extend the prior verification lease.
            return null;
        }
    }

    internal async Task<SpotifyTrackSnapshot> RecordSpotifyApplicationResponseAsync(SpotifyTrackSnapshot snapshot, CancellationToken ct)
    {
        var item = await StoreSpotifyFactsAsync(snapshot, ct);
        return snapshot with { ProviderCatalogItemId = item.Id, ProviderCatalogRevision = item.Revision };
    }

    internal async Task<IReadOnlyList<SpotifyTrackSnapshot>> ValidateSpotifySearchSnapshotsAsync(
        IReadOnlyList<SpotifyTrackSnapshot> snapshots, CancellationToken ct)
    {
        if (snapshots.Count == 0) return snapshots;
        var referencedIds = snapshots.Where(snapshot => snapshot.ProviderCatalogItemId is not null
                && snapshot.ProviderCatalogRevision is not null)
            .Select(snapshot => snapshot.Id).Distinct(StringComparer.Ordinal).ToArray();
        var items = await store.FindManyAsync("spotify", referencedIds, ct);
        var byExternalId = items.ToDictionary(item => item.ExternalId, StringComparer.Ordinal);
        var now = clock.GetUtcNow();
        var current = new List<SpotifyTrackSnapshot>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            if (snapshot.ProviderCatalogItemId is null || snapshot.ProviderCatalogRevision is null
                || !byExternalId.TryGetValue(snapshot.Id, out var item)
                || item.Id != snapshot.ProviderCatalogItemId
                || ProviderCatalogPolicy.ReadFacts(item, now) is not { } facts) continue;
            // The cached query retains its result IDs. Display and identity facts
            // come from the current independently acquired record, including when
            // another import has refreshed it. This read never renews its lease.
            current.Add(snapshot with { Name = facts.Title, Artist = facts.Artist ?? string.Empty,
                ArtistNames = facts.ArtistCredits.Select(credit => credit.Name).ToArray(),
                ArtistIdentities = facts.ArtistCredits, AlbumName = facts.Albums.FirstOrDefault(),
                ImageUrl = facts.ThumbnailUrl,
                ExternalUrl = facts.ProviderUrl ?? $"https://open.spotify.com/track/{Uri.EscapeDataString(item.ExternalId)}",
                AlbumUrl = null, DurationSeconds = facts.DurationSeconds ?? 0, Isrc = facts.Isrc,
                ProviderCatalogRevision = item.Revision });
        }
        return current;
    }

    private Task<ProviderCatalogItem> StoreSpotifyFactsAsync(SpotifyTrackSnapshot snapshot, CancellationToken ct)
    {
        var parsed = TrackMetadataParser.Parse(snapshot.Name, snapshot.Artist);
        return store.RecordVerifiedAsync("spotify", snapshot.Id, CatalogVerificationMethods.SpotifyApplication,
            new ProviderCatalogFacts
            {
                Title = snapshot.Name, Artist = snapshot.Artist,
                Albums = string.IsNullOrWhiteSpace(snapshot.AlbumName) ? [] : [snapshot.AlbumName],
                ThumbnailUrl = snapshot.ImageUrl, ProviderUrl = snapshot.ExternalUrl,
                DurationSeconds = snapshot.DurationSeconds, Isrc = snapshot.Isrc,
                ArtistCredits = snapshot.ArtistIdentities,
                VersionFlags = TrackVersionClassifier.Infer(parsed)
            }, ct);
    }

    internal Task<ProviderCatalogItem> RecordMusicBrainzResponseAsync(MusicBrainzRecordingMatch match, CancellationToken ct)
    {
        var parsed = TrackMetadataParser.Parse(match.Title, match.Artist);
        return store.RecordVerifiedAsync("musicbrainz", match.ExternalId, CatalogVerificationMethods.MusicBrainz,
            new ProviderCatalogFacts
            {
                Title = match.Title, Artist = match.Artist, DurationSeconds = match.DurationSeconds,
                Isrc = match.Isrc, MusicBrainzRecordingId = match.MbidRecording ?? match.ExternalId,
                ArtistCredits = match.ArtistIdentities,
                VersionFlags = TrackVersionClassifier.Infer(parsed)
            }, ct);
    }

}
