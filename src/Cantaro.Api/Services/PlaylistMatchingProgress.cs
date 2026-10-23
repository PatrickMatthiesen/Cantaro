using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

// Search checkpoints last for one unfinished pass. Explicit confirmations remain on
// the link after success, until the entry, its metadata, or the account binding changes.
internal sealed record PlaylistMatchingResult(string Fingerprint, string? ExternalId, int ProofVersion = 0,
    PlaylistMatchingScope? Scope = null, bool ManualConfirmation = false);

internal sealed record PlaylistMatchingScope(int UserId, Guid PlaylistId, Guid MappingId,
    int ConnectedServiceAccountId, string ExternalAccountId, string Service,
    string ServicePlaylistId, Guid EntryId);

internal static class PlaylistMatchingProgress
{
    // Reject legacy checkpoints, including version 1 results without account binding.
    public const int CurrentProofVersion = 2;

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
        var evidence = JsonSerializer.Serialize(new
        {
            entry.TrackId, entry.TrackObservationId,
            observation?.Title, observation?.Artist, observation?.DurationSeconds,
            observation?.RawMetadata, ObservationTrackId = observation?.TrackId,
            track?.CanonicalMetadata, track?.SearchTitle, track?.SearchArtist, track?.Isrc,
            track?.MbidRecording, track?.VersionFlags,
            Sources = includeProviderMappings ? track?.SourceIds.OrderBy(source => source.SourceType).ThenBy(source => source.ExternalId)
                .Select(source => new { source.SourceType, source.ExternalId }) : null
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
