using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

// Checkpoints belong to one link and one unfinished pass. Null results matter too:
// an unsuccessful search must not be repeated every time a later track hits a limit.
internal sealed record PlaylistMatchingResult(string Fingerprint, string? ExternalId, int ProofVersion = 0);

internal static class PlaylistMatchingProgress
{
    // Version 1 means the result was produced after destination identities became
    // scoped to the requesting user's attached observations or a fresh provider lookup.
    public const int CurrentProofVersion = 1;

    public static Dictionary<Guid, PlaylistMatchingResult> Read(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<Dictionary<Guid, PlaylistMatchingResult>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static string Fingerprint(PlaylistEntry entry)
    {
        var observation = entry.TrackObservation;
        var track = entry.Track;
        var evidence = JsonSerializer.Serialize(new
        {
            entry.TrackId, entry.TrackObservationId,
            observation?.Title, observation?.Artist, observation?.DurationSeconds,
            observation?.RawMetadata, ObservationTrackId = observation?.TrackId,
            track?.CanonicalMetadata, track?.SearchTitle, track?.SearchArtist, track?.Isrc,
            Sources = track?.SourceIds.OrderBy(source => source.SourceType).ThenBy(source => source.ExternalId)
                .Select(source => new { source.SourceType, source.ExternalId })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence)));
    }

    public static bool CanReuse(PlaylistMatchingResult result, string fingerprint)
        => result.ProofVersion == CurrentProofVersion && result.Fingerprint == fingerprint;
}
