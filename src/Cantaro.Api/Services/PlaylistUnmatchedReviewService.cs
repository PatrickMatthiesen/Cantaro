using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Configuration;
using Cantaro.Api.Data;
using Cantaro.Api.Models;
using Cantaro.Api.Services.Spotify;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Cantaro.Api.Services;

public sealed record PlaylistUnmatchedEntry(
    Guid EntryId, string Title, string Artist, int? DurationSeconds, string? SourceUrl);

public sealed record PlaylistMatchCandidate(
    string CandidateToken, string ExternalId, string Title, string Artist,
    int? DurationSeconds, string Url, string? Reason);

public sealed record PlaylistMatchSearch(
    PlaylistUnmatchedEntry Entry, string Reason, IReadOnlyList<PlaylistMatchCandidate> Candidates,
    string SuggestedQuery);

/// <summary>
/// Reviews destination identities without changing a playlist. Only an explicit
/// confirmation creates a mapping; the ordinary sync performs the remote write.
/// </summary>
public sealed class PlaylistUnmatchedReviewService(
    ApplicationDbContext db,
    SpotifyApiClient spotifyApi,
    SpotifyTokenManager spotifyTokens,
    IEnumerable<ITrackMetadataSearchProvider> searchProviders,
    YouTubeService youtube,
    TrackMatchingService matching,
    PlaylistCanonicalReconciliationService reconciler,
    IOptions<TrackMatchingOptions> matchingOptions,
    IDataProtectionProvider protectionProvider)
{
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("Cantaro.PlaylistSync.MatchReview.v1");
    private readonly TrackMatchingOptions _options = matchingOptions.Value;

    public async Task<IReadOnlyList<PlaylistUnmatchedEntry>> ListAsync(
        int userId, Guid playlistId, Guid mappingId, CancellationToken ct)
    {
        var link = await RequireLinkAsync(userId, playlistId, mappingId, ct);
        var entries = await LoadEntriesAsync(playlistId, ct);
        return entries.Where(entry => !HasDestinationIdentity(entry, link.Service))
            .Select(ToDto).ToArray();
    }

    public async Task<PlaylistMatchSearch> SearchAsync(
        int userId, Guid playlistId, Guid mappingId, Guid entryId, string? query, CancellationToken ct)
    {
        var link = await RequireLinkAsync(userId, playlistId, mappingId, ct);
        var entry = await RequireEntryAsync(playlistId, entryId, link.Service, ct);
        var searchText = query?.Trim();
        if (query is not null && (string.IsNullOrWhiteSpace(searchText) || searchText.Length > 200))
            throw new PlatformApiException("invalid_match_query", "Enter a search of up to 200 characters.", 400);

        await RefreshSourceEvidenceAsync(entry,
            (videoId, token) => youtube.GetVideoMetadataAsync(userId, videoId, token), ct);
        var evidence = BuildEvidence(entry);
        var parsed = TrackObservationParser.ParseSearchHypotheses(evidence)[0];
        var suggestedQuery = string.Join(" ", new[] { parsed.SearchTitle, parsed.SearchArtist }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var searched = searchText is null ? evidence : new TrackObservation
        {
            Id = evidence.Id, SourceType = evidence.SourceType, ExternalId = evidence.ExternalId,
            Title = searchText, Artist = null, DurationSeconds = evidence.DurationSeconds,
            MatchStatus = TrackMatchingStatuses.Pending
        };
        var account = new PlatformAccountContext(userId, link.ConnectedServiceAccountId, link.ExternalAccountId);
        IReadOnlyList<TrackMatchSearchCandidate> candidates = link.Service switch
        {
            "spotify" => await SearchSpotifyAsync(account, searched, searchText, ct),
            "youtube" => await youtube.SearchVideoCandidatesAsync(account, searched, ct),
            _ => throw new PlatformApiException("unsupported_platform", "This platform cannot search for recordings.", 400)
        };
        var assessment = PlaylistDestinationIdentityResolver.Assess(evidence,
            candidates.Where(candidate => candidate.CandidateSource == link.Service).ToArray(), _options);
        var result = PlaylistDestinationIdentityResolver.DistinctReviewCandidates(
            assessment.Ranked, _options).Select(scored =>
        {
            var candidate = scored.Candidate;
            var proof = new MatchProof(userId, playlistId, mappingId, entry.Id,
                link.ConnectedServiceAccountId, link.ExternalAccountId, link.Service,
                Fingerprint(entry), candidate, DateTimeOffset.UtcNow);
            return new PlaylistMatchCandidate(_protector.Protect(JsonSerializer.Serialize(proof)),
                candidate.ExternalId, candidate.Title, candidate.Artist ?? "",
                candidate.DurationSeconds, CandidateUrl(link.Service, candidate.ExternalId),
                PlaylistDestinationIdentityResolver.DescribeCandidate(scored, _options));
        }).ToArray();
        return new PlaylistMatchSearch(ToDto(entry), assessment.Reason, result, suggestedQuery);
    }

    internal async Task RefreshSourceEvidenceAsync(PlaylistEntry entry,
        Func<string, CancellationToken, Task<YouTubeVideoMetadataDto?>> readVideo,
        CancellationToken ct)
    {
        if (entry.TrackObservation is not { SourceType: "youtube" } source) return;
        var metadata = TrackObservationParser.ReadMetadata(source) ?? new TrackObservationMetadata();
        // Empty is a successfully read description; null is evidence we never fetched.
        if (metadata.Description is not null) return;
        var video = await readVideo(source.ExternalId, ct);
        if (video is null || video.VideoId != source.ExternalId) return;
        metadata.Description = video.Description ?? string.Empty;
        metadata.OriginalTitle = video.Title;
        metadata.OriginalArtist = video.ChannelTitle ?? metadata.OriginalArtist;
        metadata.ChannelTitle = video.ChannelTitle ?? metadata.ChannelTitle;
        source.RawMetadata = JsonSerializer.Serialize(metadata);
        source.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task ConfirmAsync(int userId, Guid playlistId, Guid mappingId,
        Guid entryId, string candidateToken, CancellationToken ct)
    {
        try
        {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var link = await RequireLinkAsync(userId, playlistId, mappingId, ct);
        var entry = await RequireEntryAsync(playlistId, entryId, link.Service, ct);
        var proof = ReadProof(candidateToken);
        if (proof.UserId != userId || proof.PlaylistId != playlistId || proof.MappingId != mappingId
            || proof.EntryId != entryId || proof.ConnectedServiceAccountId != link.ConnectedServiceAccountId
            || proof.ExternalAccountId != link.ExternalAccountId || proof.Service != link.Service
            || proof.Fingerprint != Fingerprint(entry)
            || DateTimeOffset.UtcNow - proof.IssuedAt > TimeSpan.FromMinutes(10)
            || proof.IssuedAt > DateTimeOffset.UtcNow.AddMinutes(1)
            || proof.Candidate.CandidateSource != link.Service
            || string.IsNullOrWhiteSpace(proof.Candidate.ExternalId)
            || string.IsNullOrWhiteSpace(proof.Candidate.Title))
            throw StaleChoice();

        // The same provider ID must never be reassigned from a different canonical
        // track. A user may attach an unresolved observation to its existing track.
        var existing = await db.TrackSourceIds.SingleOrDefaultAsync(source =>
            source.SourceType == link.Service && source.ExternalId == proof.Candidate.ExternalId, ct);
        if (existing is not null && entry.TrackId is { } currentTrackId && existing.TrackId != currentTrackId)
            throw new PlatformApiException("recording_already_linked",
                "That recording is already linked to a different Cantaro track.", 409);
        var sourceIdentity = entry.TrackObservation is { } savedObservation
            ? await db.TrackSourceIds.AsNoTracking().SingleOrDefaultAsync(source =>
                source.SourceType == savedObservation.SourceType
                && source.ExternalId == savedObservation.ExternalId, ct)
            : null;
        if (sourceIdentity is not null && sourceIdentity.TrackId != entry.TrackId)
            throw StaleChoice();

        Guid trackId;
        if (entry.TrackId is { } knownTrackId)
        {
            trackId = knownTrackId;
        }
        else if (entry.TrackObservation is { } observation)
        {
            if (observation.TrackId is not null)
                throw StaleChoice();
            if (existing is not null)
            {
                if (sourceIdentity is not null && sourceIdentity.TrackId != existing.TrackId)
                    throw new PlatformApiException("recording_already_linked",
                        "The source recording is already linked to a different Cantaro track.", 409);
                trackId = existing.TrackId;
                observation.TrackId = trackId;
                observation.MatchStatus = TrackMatchingStatuses.Matched;
                observation.ResolutionNotes = "Linked to an existing recording during playlist review.";
                observation.UpdatedAt = DateTimeOffset.UtcNow;
                if (sourceIdentity is null)
                    db.TrackSourceIds.Add(new TrackSourceId
                    {
                        Id = Guid.NewGuid(), TrackId = trackId,
                        SourceType = observation.SourceType, ExternalId = observation.ExternalId,
                        LastVerifiedAt = DateTimeOffset.UtcNow,
                        OriginMetadata = "{\"method\":\"playlist_manual_review\"}"
                    });
                await reconciler.ReconcileObservationAsync(observation.Id, trackId, ct);
            }
            else
            {
                if (sourceIdentity is not null) throw StaleChoice();
                var resolved = await matching.CreateTrackFromMatchAsync(observation.Id, proof.Candidate,
                    ct, manualReview: true);
                trackId = resolved.TrackId ?? throw new InvalidOperationException("The recording was not created.");
                existing = await db.TrackSourceIds.SingleOrDefaultAsync(source =>
                    source.SourceType == link.Service && source.ExternalId == proof.Candidate.ExternalId, ct);
            }
        }
        else throw StaleChoice();

        if (existing is null)
        {
            db.TrackSourceIds.Add(new TrackSourceId
            {
                Id = Guid.NewGuid(), TrackId = trackId,
                SourceType = link.Service, ExternalId = proof.Candidate.ExternalId,
                LastVerifiedAt = DateTimeOffset.UtcNow,
                OriginMetadata = JsonSerializer.Serialize(new
                {
                    method = "playlist_manual_review",
                    title = proof.Candidate.Title,
                    artist = proof.Candidate.Artist,
                    durationSeconds = proof.Candidate.DurationSeconds
                })
            });
        }
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: "23505" or "40001" })
        {
            throw new PlatformApiException("match_changed",
                "Another match changed while you confirmed this recording. Search again.", 409);
        }
        catch (PostgresException ex) when (ex.SqlState == "40001")
        {
            throw new PlatformApiException("match_changed",
                "Another match changed while you confirmed this recording. Search again.", 409);
        }
    }

    private async Task<IReadOnlyList<TrackMatchSearchCandidate>> SearchSpotifyAsync(
        PlatformAccountContext account, TrackObservation evidence, string? query, CancellationToken ct)
    {
        var token = await spotifyTokens.GetAccessTokenSnapshotAsync(account, false, ct);
        var profile = await spotifyApi.GetProfileAsync(token.Value, ct);
        if (!string.Equals(profile.AccountId ?? profile.Id, account.ExpectedExternalAccountId, StringComparison.Ordinal))
            throw new PlatformApiException("spotify_account_changed", "Reconnect the linked Spotify account.", 409);
        if (query is null)
            return await searchProviders.OfType<SpotifySearchProvider>().Single().SearchAsync(evidence, ct);
        var text = query;
        var tracks = await spotifyApi.SearchTracksAsync(token.Value, text, 10, ct);
        return tracks.Select(track => new TrackMatchSearchCandidate
        {
            CandidateSource = "spotify", ExternalId = track.Id,
            Title = track.Name, Artist = track.Artist,
            ArtistCredits = track.ArtistNames, Isrc = track.Isrc,
            DurationSeconds = track.DurationSeconds
        }).ToArray();
    }

    private async Task<ServicePlaylistMapping> RequireLinkAsync(
        int userId, Guid playlistId, Guid mappingId, CancellationToken ct)
    {
        var link = await db.ServicePlaylistMappings.AsNoTracking()
            .Include(item => item.ConnectedServiceAccount)
            .SingleOrDefaultAsync(item => item.Id == mappingId && item.UserId == userId
                && item.PlaylistId == playlistId && item.State == "active"
                && item.Playlist!.UserId == userId, ct);
        var account = link?.ConnectedServiceAccount;
        if (link is null || account is null || account.UserId != userId
            || account.Id != link.ConnectedServiceAccountId || account.Service != link.Service
            || account.ConnectionState != "connected" || account.ExternalAccountId != link.ExternalAccountId)
            throw new PlatformApiException("playlist_link_not_found", "Playlist link not found or disconnected.", 404);
        return link;
    }

    private async Task<PlaylistEntry> RequireEntryAsync(Guid playlistId, Guid entryId, string service, CancellationToken ct)
    {
        var entry = await db.PlaylistEntries
            .Include(item => item.TrackObservation)
            .Include(item => item.Track).ThenInclude(item => item!.SourceIds)
            .SingleOrDefaultAsync(item => item.PlaylistId == playlistId && item.Id == entryId, ct);
        if (entry is null || HasDestinationIdentity(entry, service)
            || (entry.TrackId is null && entry.TrackObservation is null))
            throw new PlatformApiException("unmatched_entry_not_found", "That playlist entry no longer needs a match.", 404);
        return entry;
    }

    private Task<List<PlaylistEntry>> LoadEntriesAsync(Guid playlistId, CancellationToken ct)
        => db.PlaylistEntries.AsNoTracking().Where(item => item.PlaylistId == playlistId)
            .Include(item => item.TrackObservation)
            .Include(item => item.Track).ThenInclude(item => item!.SourceIds)
            .OrderBy(item => item.Position).ToListAsync(ct);

    private static bool HasDestinationIdentity(PlaylistEntry entry, string service)
        => entry.TrackObservation is { } observation && observation.SourceType == service
            && (entry.TrackId is null || observation.TrackId == entry.TrackId)
            || entry.Track?.SourceIds.Any(source => source.SourceType == service) == true;

    private static PlaylistUnmatchedEntry ToDto(PlaylistEntry entry)
    {
        var observation = entry.TrackObservation;
        TrackCanonicalMetadata? canonical = null;
        try { canonical = JsonSerializer.Deserialize<TrackCanonicalMetadata>(entry.Track?.CanonicalMetadata ?? "null"); }
        catch (JsonException) { /* Fall back to the saved observation. */ }
        var metadata = observation is null ? null : TrackObservationParser.ReadMetadata(observation);
        var sourceUrl = observation is not null
            && System.Text.RegularExpressions.Regex.IsMatch(observation.ExternalId, "^[A-Za-z0-9_-]{1,128}$")
            ? CandidateUrl(observation.SourceType, observation.ExternalId) : null;
        return new PlaylistUnmatchedEntry(entry.Id,
            metadata?.OriginalTitle ?? canonical?.Title ?? entry.Track?.SearchTitle ?? observation?.Title ?? "Unknown track",
            metadata?.OriginalArtist ?? canonical?.Artist ?? entry.Track?.SearchArtist ?? observation?.Artist ?? "",
            canonical?.DurationSeconds ?? observation?.DurationSeconds, sourceUrl);
    }

    private static TrackObservation BuildEvidence(PlaylistEntry entry)
    {
        var source = entry.TrackObservation;
        var dto = ToDto(entry);
        var metadata = source is null ? new TrackObservationMetadata() :
            TrackObservationParser.ReadMetadata(source) ?? new TrackObservationMetadata();
        metadata.Isrc = TrackIdentityResolver.NormalizeIsrc(entry.Track?.Isrc)
            ?? TrackIdentityResolver.NormalizeIsrc(metadata.Isrc);
        return new TrackObservation
        {
            Id = source?.Id ?? Guid.NewGuid(),
            SourceType = source?.SourceType ?? "cantaro",
            ExternalId = source?.ExternalId ?? entry.TrackId?.ToString() ?? "",
            Title = source?.Title ?? dto.Title,
            Artist = source?.Artist ?? dto.Artist,
            DurationSeconds = source?.DurationSeconds ?? dto.DurationSeconds,
            RawMetadata = JsonSerializer.Serialize(metadata),
            MatchStatus = TrackMatchingStatuses.Pending
        };
    }

    private static string CandidateUrl(string service, string id) => service switch
    {
        "spotify" => $"https://open.spotify.com/track/{Uri.EscapeDataString(id)}",
        "youtube" => $"https://www.youtube.com/watch?v={Uri.EscapeDataString(id)}",
        _ => ""
    };

    private static string Fingerprint(PlaylistEntry entry)
    {
        var value = JsonSerializer.Serialize(new
        {
            entry.Id, entry.PlaylistId, entry.TrackId, entry.TrackObservationId,
            entry.TrackObservation?.UpdatedAt, entry.TrackObservation?.Title,
            entry.TrackObservation?.Artist, entry.TrackObservation?.DurationSeconds,
            entry.Track?.CanonicalMetadata
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private MatchProof ReadProof(string token)
    {
        try
        {
            return JsonSerializer.Deserialize<MatchProof>(_protector.Unprotect(token)) ?? throw StaleChoice();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        {
            throw StaleChoice();
        }
    }

    private static PlatformApiException StaleChoice() => new("stale_match_choice",
        "That match has expired or the playlist changed. Search again before confirming.", 409);

    private sealed record MatchProof(int UserId, Guid PlaylistId, Guid MappingId, Guid EntryId,
        int ConnectedServiceAccountId, string ExternalAccountId, string Service,
        string Fingerprint, TrackMatchSearchCandidate Candidate, DateTimeOffset IssuedAt);
}
