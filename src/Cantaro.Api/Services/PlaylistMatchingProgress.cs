using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

// Successful destination choices remain scoped to the link after export, until
// the entry, its metadata, or the account binding changes. Manual choices take priority.
internal sealed record PlaylistMatchingResult(string Fingerprint, string? ExternalId, int ProofVersion = 0,
    PlaylistMatchingScope? Scope = null, bool ManualConfirmation = false);

internal sealed record PlaylistMatchingScope(int UserId, Guid PlaylistId, Guid MappingId,
    int ConnectedServiceAccountId, string ExternalAccountId, string Service,
    string ServicePlaylistId, Guid EntryId);

internal static class PlaylistMatchingProgress
{
    // Reject legacy checkpoints, including version 1 results without account binding.
    public const int CurrentProofVersion = 4;

    public static Dictionary<Guid, PlaylistMatchingResult> Read(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<Guid, PlaylistMatchingResult>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static string Fingerprint(PlaylistEntry entry, bool includeProviderMappings = true)
    {
        var observation = entry.TrackObservation;
        var track = entry.Track;
        var displaySource = ProviderCatalogPolicy.SharedSource(track, DateTimeOffset.UtcNow);
        var evidence = JsonSerializer.Serialize(new
        {
            entry.OwnerUserId, entry.TrackId, entry.TrackObservationId,
            ObservationOwnerUserId = observation?.OwnerUserId, observation?.InputKind, ObservationTrackId = observation?.TrackId,
            observation?.SourceType, observation?.ExternalId, observation?.ProviderCatalogItemId,
            // Public metadata is referenced by its facts generation. Lease
            // renewal alone must not erase an otherwise unchanged manual choice.
            InputRevision = observation?.InputKind == ObservationInputKind.Public
                ? observation.ProviderCatalogItem?.Revision : null,
            Title = observation?.InputKind == ObservationInputKind.Private ? observation.Title : null,
            Artist = observation?.InputKind == ObservationInputKind.Private ? observation.Artist : null,
            DurationSeconds = observation?.InputKind == ObservationInputKind.Private ? observation.DurationSeconds : null,
            PrivateInputJson = observation?.InputKind == ObservationInputKind.Private ? observation.PrivateInputJson : null,
            track?.Isrc, track?.MbidRecording, track?.VersionFlags,
            DisplaySourceId = displaySource?.Id, DisplayFactsRevision = displaySource?.Revision,
            Sources = includeProviderMappings ? track?.ProviderItems.OrderBy(source => source.SourceType, StringComparer.Ordinal)
                .ThenBy(source => source.ExternalId, StringComparer.Ordinal)
                .Select(source => new { source.SourceType, source.ExternalId, source.AssociationState,
                    source.Id, source.Revision, source.AssociationRevision, source.AssociationFactsRevision,
                    Current = ProviderCatalogPolicy.IsVerified(source, DateTimeOffset.UtcNow) }) : null
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence)));
    }

    public static PlaylistMatchingScope ScopeFor(ServicePlaylistMapping link, PlaylistEntry entry)
        => new(link.UserId, link.PlaylistId, link.Id, link.ConnectedServiceAccountId,
            link.ExternalAccountId, link.Service, link.ServicePlaylistId, entry.Id);

    public static PlaylistMatchingResult Create(ServicePlaylistMapping link, PlaylistEntry entry,
        string? externalId, bool manualConfirmation = false)
        => new(Fingerprint(entry, includeProviderMappings: !manualConfirmation), externalId,
            CurrentProofVersion, ScopeFor(link, entry), manualConfirmation);

    public static bool CanReuse(PlaylistMatchingResult result, PlaylistEntry entry, ServicePlaylistMapping link)
        => result.ProofVersion == CurrentProofVersion && result.Scope == ScopeFor(link, entry)
            && result.Fingerprint == Fingerprint(entry, includeProviderMappings: !result.ManualConfirmation);

    public static string? Serialize(Dictionary<Guid, PlaylistMatchingResult> progress)
        => progress.Count == 0 ? null : JsonSerializer.Serialize(progress);
}
