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
    IDataProtectionProvider protectionProvider,
    PlaylistSyncCoordinator coordinator)
{
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("Cantaro.PlaylistSync.MatchReview.v1");
    private readonly TrackMatchingOptions _options = matchingOptions.Value;

    public async Task<IReadOnlyList<PlaylistUnmatchedEntry>> ListAsync(
        int userId, Guid playlistId, Guid mappingId, CancellationToken ct)
    {
        var link = await RequireLinkAsync(userId, playlistId, mappingId, ct);
        var entries = await LoadEntriesAsync(playlistId, ct);
        var identities = await PlaylistDestinationIdentityEvidence.FindManyAsync(db,
            new PlatformAccountContext(link.UserId, link.ConnectedServiceAccountId, link.ExternalAccountId),
            link.Service, entries, link, ct);
        var supporting = await LoadSupportingInputsAsync(userId, entries, ct);
        var unmatched = new List<PlaylistUnmatchedEntry>();
        foreach (var entry in entries)
            if (!identities.ContainsKey(entry.Id)) unmatched.Add(ToDto(entry, userId, supporting.GetValueOrDefault(entry.TrackId ?? Guid.Empty)));
        return unmatched;
    }

    public async Task<PlaylistMatchSearch> SearchAsync(
        int userId, Guid playlistId, Guid mappingId, Guid entryId, string? query, CancellationToken ct)
    {
        var link = await RequireLinkAsync(userId, playlistId, mappingId, ct);
        var entry = await RequireEntryAsync(playlistId, entryId, link, ct);
        if (entry.TrackObservation is { } observation
            && observation.OwnerUserId != userId) throw StaleChoice();
        var searchText = query?.Trim();
        if (query is not null && (string.IsNullOrWhiteSpace(searchText) || searchText.Length > 200))
            throw new PlatformApiException("invalid_match_query", "Enter a search of up to 200 characters.", 400);

        await RefreshSourceEvidenceAsync(entry,
            (videoId, token) => youtube.GetVideoMetadataAsync(userId, videoId, token), ct);
        var supporting = (await LoadSupportingInputsAsync(userId, [entry], ct)).GetValueOrDefault(entry.TrackId ?? Guid.Empty);
        var evidence = BuildEvidence(entry, userId, supporting);
        var parsed = TrackObservationParser.ParseSearchHypotheses(evidence)[0];
        var suggestedQuery = string.Join(" ", new[] { parsed.SearchTitle, parsed.SearchArtist }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var searched = searchText is null ? evidence : evidence with { Title = searchText, Artist = null };
        var account = new PlatformAccountContext(userId, link.ConnectedServiceAccountId, link.ExternalAccountId);
        IReadOnlyList<TrackMatchSearchCandidate> candidates;
        long? searchedTokenVersion = null;
        if (link.Service == "spotify")
            (candidates, searchedTokenVersion) = await SearchSpotifyAsync(account, searched, searchText, ct);
        else if (link.Service == "youtube")
            candidates = await youtube.SearchVideoCandidatesAsync(account, searched, ct);
        else throw new PlatformApiException("unsupported_platform", "This platform cannot search for recordings.", 400);
        var currentLink = await RequireLinkAsync(userId, playlistId, mappingId, ct);
        if (currentLink.ConnectedServiceAccountId != link.ConnectedServiceAccountId
            || currentLink.ExternalAccountId != link.ExternalAccountId
            || currentLink.ServicePlaylistId != link.ServicePlaylistId
            || searchedTokenVersion is { } version && currentLink.ConnectedServiceAccount!.TokenVersion != version)
            throw StaleChoice();
        link = currentLink;
        var assessment = PlaylistDestinationIdentityResolver.Assess(evidence,
            candidates.Where(candidate => candidate.CandidateSource == link.Service).ToArray(), _options);
        // A user can search for a destination even when a legacy entry has no
        // verified display facts. Low similarity must not hide search results
        // from explicit review or make an unknown entry impossible to recover.
        var reviewCandidates = assessment.Ranked.Count > 0 ? assessment.Ranked
            : candidates.Where(candidate => candidate.CandidateSource == link.Service)
                .Select(candidate => TrackMatchScorer.Score(evidence, candidate, _options))
                .OrderByDescending(candidate => candidate.Score).ToArray();
        var result = PlaylistDestinationIdentityResolver.DistinctReviewCandidates(
            reviewCandidates, _options).Select(scored =>
        {
            var candidate = scored.Candidate;
            var proof = new MatchProof(userId, playlistId, mappingId, entry.Id,
                link.ConnectedServiceAccountId, link.ExternalAccountId, link.Service,
                Fingerprint(entry), candidate, DateTimeOffset.UtcNow,
                link.ServicePlaylistId, link.ConnectedServiceAccount!.TokenVersion,
                supporting is null ? null : SupportingFingerprint(supporting));
            return new PlaylistMatchCandidate(_protector.Protect(JsonSerializer.Serialize(proof)),
                candidate.ExternalId, candidate.Title, candidate.Artist ?? "",
                candidate.DurationSeconds, CandidateUrl(link.Service, candidate.ExternalId),
                PlaylistDestinationIdentityResolver.DescribeCandidate(scored, _options));
        }).ToArray();
        return new PlaylistMatchSearch(ToDto(entry, userId, supporting), assessment.Reason, result, suggestedQuery);
    }

    internal async Task RefreshSourceEvidenceAsync(PlaylistEntry entry,
        Func<string, CancellationToken, Task<YouTubeVideoMetadataDto?>> readVideo,
        CancellationToken ct)
    {
        if (entry.TrackObservation is not { SourceType: "youtube" } source) return;
        if (!ProviderCatalogPolicy.HasOwnedInput(source, entry.Playlist?.UserId)) return;
        var input = ObservationInput.Read(source, source.OwnerUserId, DateTimeOffset.UtcNow);
        if (input is null) return;
        var metadata = input.Metadata;
        // Empty is a successfully read description; null is evidence we never fetched.
        if (metadata.Description is not null) return;
        var video = await readVideo(source.ExternalId, ct);
        if (video is null || video.VideoId != source.ExternalId) return;
        metadata.Description = video.Description ?? string.Empty;
        metadata.OriginalTitle = video.Title;
        metadata.OriginalArtist = video.ChannelTitle ?? metadata.OriginalArtist;
        metadata.ChannelTitle = video.ChannelTitle ?? metadata.ChannelTitle;
        // Only private input may retain authenticated response evidence. Public input
        // is refreshed by the provider's independent catalog acquisition.
        if (source.InputKind == ObservationInputKind.Public)
        {
            if (source.ProviderCatalogItem is not null) await db.Entry(source.ProviderCatalogItem).ReloadAsync(ct);
            else if (source.ProviderCatalogItemId is not null) await db.Entry(source).Reference(o => o.ProviderCatalogItem).LoadAsync(ct);
            return;
        }
        ObservationInput.SetPrivate(source, input.Title, input.Artist, input.DurationSeconds, input.ThumbnailUrl, metadata);
        source.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public Task ConfirmAsync(int userId, Guid playlistId, Guid mappingId,
        Guid entryId, string candidateToken, CancellationToken ct)
        // Confirmation and sync both replace MatchingProgressJson. Take the same
        // lease before loading mutable evidence or starting the transaction.
        => coordinator.WithPlaylistLeaseAsync(userId, playlistId,
            token => new PlaylistSyncCoordinator.PlaylistTransactionStrategy(db).ExecuteAsync(
                () => ConfirmCoreAsync(userId, playlistId, mappingId, entryId, candidateToken, token)), ct);

    private async Task ConfirmCoreAsync(int userId, Guid playlistId, Guid mappingId,
        Guid entryId, string candidateToken, CancellationToken ct)
    {
        try
        {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var link = await RequireLinkAsync(userId, playlistId, mappingId, ct);
        var entry = await RequireEntryAsync(playlistId, entryId, link, ct, requireUnmatched: false);
        if (entry.TrackObservation is { } ownerObservation
            && ownerObservation.OwnerUserId != userId) throw StaleChoice();
        var proof = ReadProof(candidateToken);
        if (proof.UserId != userId || proof.PlaylistId != playlistId || proof.MappingId != mappingId
            || proof.EntryId != entryId || proof.ConnectedServiceAccountId != link.ConnectedServiceAccountId
            || proof.ExternalAccountId != link.ExternalAccountId || proof.Service != link.Service
            || proof.ServicePlaylistId != link.ServicePlaylistId
            || proof.TokenVersion != link.ConnectedServiceAccount!.TokenVersion
            || DateTimeOffset.UtcNow - proof.IssuedAt > TimeSpan.FromMinutes(10)
            || proof.IssuedAt > DateTimeOffset.UtcNow.AddMinutes(1)
            || proof.Candidate.CandidateSource != link.Service
            || string.IsNullOrWhiteSpace(proof.Candidate.ExternalId)
            || string.IsNullOrWhiteSpace(proof.Candidate.Title))
            throw StaleChoice();

        // Keep canonical identity conflicts independent of destination eligibility,
        // including retries whose scoped confirmation is already persisted.
        var existing = await db.ProviderCatalogItems.SingleOrDefaultAsync(source =>
            source.SourceType == link.Service && source.ExternalId == proof.Candidate.ExternalId, ct);
        if (existing is not null && !ProviderCatalogPolicy.IsVerified(existing, DateTimeOffset.UtcNow)) existing = null;
        if (existing is not null && entry.TrackId is { } currentTrackId && existing.TrackId != currentTrackId)
            throw new PlatformApiException("recording_already_linked",
                "That recording is already linked to a different Cantaro track.", 409);

        var progress = PlaylistMatchingProgress.Read(link.MatchingProgressJson);
        if (progress.TryGetValue(entry.Id, out var confirmed) && confirmed.ManualConfirmation
            && PlaylistMatchingProgress.CanReuse(confirmed, entry, link)
            && confirmed.ExternalId == proof.Candidate.ExternalId
            && await HasDestinationIdentityAsync(entry, link, ct)) return;
        if (proof.Fingerprint != Fingerprint(entry)) throw StaleChoice();
        if (proof.SupportingInputFingerprint is not null)
        {
            var current = (await LoadSupportingInputsAsync(userId, [entry], ct))
                .GetValueOrDefault(entry.TrackId ?? Guid.Empty);
            if (current is null || proof.SupportingInputFingerprint != SupportingFingerprint(current)) throw StaleChoice();
        }
        if (await HasDestinationIdentityAsync(entry, link, ct))
            throw new PlatformApiException("unmatched_entry_not_found", "That playlist entry no longer needs a match.", 404);

        var sourceIdentity = entry.TrackObservation is { } savedObservation
            ? await db.ProviderCatalogItems.AsNoTracking().SingleOrDefaultAsync(source =>
                source.SourceType == savedObservation.SourceType
                && source.ExternalId == savedObservation.ExternalId, ct)
            : null;
        if (sourceIdentity is not null && !ProviderCatalogPolicy.IsVerified(sourceIdentity, DateTimeOffset.UtcNow))
            sourceIdentity = null;
        if (sourceIdentity is not null && sourceIdentity.TrackId != entry.TrackId)
            throw StaleChoice();

        // Capture this before unresolved-source materialization can add a mapping
        // or copy the selected recording's identifiers onto a canonical track.
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
                trackId = existing.TrackId!.Value;
                observation.TrackId = trackId;
                observation.MatchStatus = TrackMatchingStatuses.Matched;
                observation.ResolutionNotes = "Linked to an existing recording during playlist review.";
                observation.UpdatedAt = DateTimeOffset.UtcNow;
                await reconciler.ReconcileObservationAsync(observation.Id, trackId, ct);
            }
            else
            {
                if (sourceIdentity is not null) throw StaleChoice();
                var ownedEntries = await LoadEntriesAsync(playlistId, ct);
                var previousTracks = ownedEntries.Where(item => item.TrackId is not null
                    && progress.TryGetValue(item.Id, out var receipt) && receipt.ManualConfirmation
                    && receipt.ExternalId == proof.Candidate.ExternalId && PlaylistMatchingProgress.CanReuse(receipt, item, link))
                    .Select(item => item.TrackId!.Value).Distinct().Take(2).ToArray();
                if (previousTracks.Length == 1)
                {
                    trackId = previousTracks[0];
                    observation.TrackId = trackId;
                    observation.MatchStatus = TrackMatchingStatuses.Matched;
                    observation.UpdatedAt = DateTimeOffset.UtcNow;
                    await reconciler.ReconcileObservationAsync(observation.Id, trackId, ct);
                }
                else
                {
                    var resolved = await matching.CreateTrackFromMatchAsync(observation.Id, proof.Candidate,
                        ct, manualReview: true);
                    trackId = resolved.TrackId ?? throw new InvalidOperationException("The recording was not created.");
                }
            }
        }
        else throw StaleChoice();

        // Manual destination confirmation is scoped to this playlist and does not
        // promote provider identity or artist credits into the shared catalog.

        // An existing canonical entry needs a destination choice, not a new
        // global identity assertion. Unresolved-source materialization above
        // owns its canonical mappings; this choice is authorized by scoped proof.
        await db.SaveChangesAsync(ct);
        // Reconciliation can replace the original entry with an existing canonical
        // entry. Authorize only the survivor in this requester's playlist and link.
        var retained = await db.PlaylistEntries.AsNoTracking()
            .Include(item => item.TrackObservation).ThenInclude(observation => observation!.ProviderCatalogItem)
            .Include(item => item.Track).ThenInclude(track => track!.ProviderItems)
            .Where(item => item.PlaylistId == playlistId && item.TrackId == trackId)
            .OrderBy(item => item.Id == entryId ? 0 : 1).ThenBy(item => item.Position).FirstOrDefaultAsync(ct)
            ?? throw StaleChoice();
        var savedLink = await db.ServicePlaylistMappings.SingleAsync(item => item.Id == mappingId, ct);
        progress = PlaylistMatchingProgress.Read(savedLink.MatchingProgressJson);
        progress.Remove(entryId);
        progress[retained.Id] = PlaylistMatchingProgress.Create(link, retained, proof.Candidate.ExternalId,
            manualConfirmation: true);
        savedLink.MatchingProgressJson = PlaylistMatchingProgress.Serialize(progress);
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

    private async Task<(IReadOnlyList<TrackMatchSearchCandidate> Candidates, long TokenVersion)> SearchSpotifyAsync(
        PlatformAccountContext account, ObservationInput evidence, string? query, CancellationToken ct)
    {
        var token = await spotifyTokens.GetAccessTokenSnapshotAsync(account, false, ct);
        var profile = await spotifyApi.GetProfileAsync(token.Value, ct);
        if (!string.Equals(profile.AccountId ?? profile.Id, account.ExpectedExternalAccountId, StringComparison.Ordinal))
            throw new PlatformApiException("spotify_account_changed", "Reconnect the linked Spotify account.", 409);
        if (query is null)
            return (await searchProviders.OfType<SpotifySearchProvider>().Single().SearchAsync(evidence, ct), token.Version);
        var text = query;
        var tracks = await spotifyApi.SearchTracksAsync(token.Value, text, 10, ct);
        return (tracks.Select(track => new TrackMatchSearchCandidate
        {
            CandidateSource = "spotify", ExternalId = track.Id,
            Title = track.Name, Artist = track.Artist,
            ArtistCredits = track.ArtistNames, Isrc = track.Isrc,
            ArtistIdentities = track.ArtistIdentities,
            DurationSeconds = track.DurationSeconds
        }).ToArray(), token.Version);
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

    private async Task<PlaylistEntry> RequireEntryAsync(Guid playlistId, Guid entryId,
        ServicePlaylistMapping link, CancellationToken ct, bool requireUnmatched = true)
    {
        var entry = await db.PlaylistEntries
            .Include(item => item.TrackObservation).ThenInclude(observation => observation!.ProviderCatalogItem)
            .Include(item => item.Track).ThenInclude(item => item!.ProviderItems)
            .SingleOrDefaultAsync(item => item.PlaylistId == playlistId && item.Id == entryId, ct);
        if (entry is null || requireUnmatched && await HasDestinationIdentityAsync(entry, link, ct)
            || (entry.TrackId is null && entry.TrackObservation is null))
            throw new PlatformApiException("unmatched_entry_not_found", "That playlist entry no longer needs a match.", 404);
        return entry;
    }

    private Task<List<PlaylistEntry>> LoadEntriesAsync(Guid playlistId, CancellationToken ct)
        => db.PlaylistEntries.AsNoTracking().Where(item => item.PlaylistId == playlistId)
            .Include(item => item.TrackObservation).ThenInclude(observation => observation!.ProviderCatalogItem)
            .Include(item => item.Track).ThenInclude(item => item!.ProviderItems)
            .OrderBy(item => item.Position).ToListAsync(ct);

    private async Task<bool> HasDestinationIdentityAsync(PlaylistEntry entry, ServicePlaylistMapping link, CancellationToken ct)
        => await PlaylistDestinationIdentityEvidence.FindAsync(db,
            new PlatformAccountContext(link.UserId, link.ConnectedServiceAccountId, link.ExternalAccountId),
            link.Service, entry, ct, link) is not null;

    // Membership, rather than an observation's processing result, associates input with a recording.
    // Load all missing recordings together so listing does not issue queries for each entry.
    private async Task<Dictionary<Guid, ObservationInput>> LoadSupportingInputsAsync(
        int userId, IEnumerable<PlaylistEntry> entries, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = entries.Where(entry => entry.TrackId is not null
                && ObservationInput.Read(entry.TrackObservation, userId, now) is null
                && ProviderCatalogPolicy.SharedFacts(entry.Track, now) is null)
            .Select(entry => entry.TrackId!.Value).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var eligible = ObservationInput.Eligible(db.TrackObservations.AsNoTracking(), now)
            .Where(input => input.OwnerUserId == userId).Select(input => input.Id);
        var members = await db.PlaylistEntries.AsNoTracking()
            .Where(entry => entry.TrackId != null && ids.Contains(entry.TrackId.Value)
                && entry.Playlist!.UserId == userId && entry.TrackObservationId != null
                && eligible.Contains(entry.TrackObservationId.Value))
            .Include(entry => entry.TrackObservation).ThenInclude(input => input!.ProviderCatalogItem)
            .OrderBy(entry => entry.Id).ToListAsync(ct);
        var inputs = new Dictionary<Guid, ObservationInput>();
        foreach (var member in members)
        {
            var input = ObservationInput.Read(member.TrackObservation, userId, now);
            if (string.IsNullOrWhiteSpace(input?.Title) || string.IsNullOrWhiteSpace(input.Artist)) continue;
            inputs.TryAdd(member.TrackId!.Value, input);
        }
        var anchors = await OwnedCandidateAnchorInput.LoadAsync(db, userId,
            ids.Where(id => !inputs.ContainsKey(id)), ct, requireArtist: true);
        foreach (var (id, input) in anchors) inputs.TryAdd(id, input);
        return inputs;
    }

    private static PlaylistUnmatchedEntry ToDto(PlaylistEntry entry, int userId, ObservationInput? supporting = null)
    {
        var observation = ObservationInput.Read(entry.TrackObservation, userId, DateTimeOffset.UtcNow);
        var canonical = ProviderCatalogPolicy.SharedFacts(entry.Track, DateTimeOffset.UtcNow);
        observation ??= canonical is null ? supporting : null;
        var metadata = observation is null ? null : observation.Metadata;
        var sourceUrl = observation is not null
            && System.Text.RegularExpressions.Regex.IsMatch(observation.ExternalId, "^[A-Za-z0-9_-]{1,128}$")
            ? CandidateUrl(observation.SourceType, observation.ExternalId) : null;
        return new PlaylistUnmatchedEntry(entry.Id,
            metadata?.OriginalTitle ?? canonical?.Title ?? observation?.Title ?? "Unknown track",
            metadata?.OriginalArtist ?? canonical?.Artist ?? observation?.Artist ?? "",
            canonical?.DurationSeconds ?? observation?.DurationSeconds, sourceUrl);
    }

    private static ObservationInput BuildEvidence(PlaylistEntry entry, int userId, ObservationInput? supporting = null)
    {
        var source = ObservationInput.Read(entry.TrackObservation, userId, DateTimeOffset.UtcNow);
        if (source is not null) return source;
        var facts = ProviderCatalogPolicy.SharedFacts(entry.Track, DateTimeOffset.UtcNow);
        if (facts is null && supporting is not null) return supporting;
        return ObservationInput.FromFacts("cantaro", entry.TrackId?.ToString() ?? "",
            facts ?? new ProviderCatalogFacts { Title = "Unknown track" });
    }

    private static string CandidateUrl(string service, string id) => service switch
    {
        "spotify" => $"https://open.spotify.com/track/{Uri.EscapeDataString(id)}",
        "youtube" => $"https://www.youtube.com/watch?v={Uri.EscapeDataString(id)}",
        _ => ""
    };

    private static string SupportingFingerprint(ObservationInput input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));

    private static string Fingerprint(PlaylistEntry entry)
    {
        var value = JsonSerializer.Serialize(new
        {
            entry.Id, entry.PlaylistId, entry.TrackId, entry.TrackObservationId,
            entry.TrackObservation?.UpdatedAt,
            Input = ObservationInput.Read(entry.TrackObservation, entry.OwnerUserId, DateTimeOffset.UtcNow),
            ObservationTrackId = entry.TrackObservation?.TrackId,
            Sources = entry.Track?.ProviderItems.OrderBy(item => item.SourceType, StringComparer.Ordinal)
                .ThenBy(item => item.ExternalId, StringComparer.Ordinal)
                .Select(item => new { item.Id, item.Revision, item.AssociationRevision, item.AssociationFactsRevision }),
            entry.Track?.Isrc, entry.Track?.MbidRecording, entry.Track?.VersionFlags
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
        string Fingerprint, TrackMatchSearchCandidate Candidate, DateTimeOffset IssuedAt,
        string ServicePlaylistId, long TokenVersion, string? SupportingInputFingerprint = null);
}
