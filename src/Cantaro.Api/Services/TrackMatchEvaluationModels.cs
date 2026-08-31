namespace Cantaro.Api.Services;

internal sealed class TrackMatchScoredCandidate
{
    public required TrackMatchSearchCandidate Candidate { get; init; }
    public required string ObservationTitle { get; init; }
    public string? ObservationArtist { get; init; }
    public required ParsedTrackMetadata ObservationMetadata { get; init; }
    public required ParsedTrackMetadata CandidateMetadata { get; init; }
    public decimal TitleSimilarity { get; init; }
    public decimal ArtistSimilarity { get; init; }
    public decimal DurationScore { get; init; }
    public decimal SemanticAdjustment { get; init; }
    public required string SemanticExplanation { get; init; }
    public decimal Score { get; init; }
    public int? ObservationDurationSeconds { get; init; }
    public int? DurationDifferenceSeconds { get; init; }
    public bool HasEquivalentArtistCredits { get; init; }
    public bool HasCompatibleSemantics { get; init; }
    public bool IsAutoMatchEligible { get; init; }
    public required string AutoMatchEligibilityReason { get; init; }
}

internal sealed class TrackMatchIdentityFamily
{
    public required string FamilyId { get; init; }
    public required TrackMatchScoredCandidate Representative { get; init; }
    public required IReadOnlyList<TrackMatchScoredCandidate> Members { get; init; }
    public int ProviderConsensusCount { get; init; }
}

internal sealed class TrackMatchCluster
{
    public required string ClusterId { get; init; }
    public required string ClusterReason { get; init; }
    public required TrackMatchScoredCandidate Representative { get; init; }
    public required IReadOnlyList<TrackMatchScoredCandidate> Members { get; init; }
}

internal sealed class TrackMatchDecision
{
    public required string MatchStatus { get; init; }
    public required string ResolutionNotes { get; init; }
    public TrackMatchScoredCandidate? AcceptedCandidate { get; init; }
    public decimal TopScore { get; init; }
    public decimal SecondDistinctScore { get; init; }
    public required string DecisionReason { get; init; }
}

public sealed class TrackMatchCandidateStoredMetadata
{
    public string? ProviderRawMetadata { get; set; }
    public string? ArtistMusicBrainzId { get; set; }
    public string? ArtistSortName { get; set; }
    public TrackMatchCandidateDiagnostics? Matching { get; set; }
}

public sealed class TrackMatchCandidateDiagnostics
{
    public string? ObservationSearchTitle { get; set; }
    public string? ObservationSearchArtist { get; set; }
    public string? CandidateSearchTitle { get; set; }
    public string? CandidateSearchArtist { get; set; }
    public List<string> ObservationVersionMarkers { get; set; } = [];
    public List<string> ObservationPlaybackModifiers { get; set; } = [];
    public List<string> CandidateVersionMarkers { get; set; } = [];
    public List<string> CandidatePlaybackModifiers { get; set; } = [];
    public decimal TitleSimilarity { get; set; }
    public decimal ArtistSimilarity { get; set; }
    public decimal DurationScore { get; set; }
    public decimal SemanticAdjustment { get; set; }
    public decimal TotalScore { get; set; }
    public string? SemanticExplanation { get; set; }
    public string? ClusterId { get; set; }
    public int ClusterSize { get; set; }
    public string? ClusterReason { get; set; }
}

public sealed class TrackMatchObservationDiagnostics
{
    public List<string> VersionMarkers { get; set; } = [];
    public List<string> PlaybackModifiers { get; set; } = [];
    public string? DecisionReason { get; set; }
    public decimal? TopScore { get; set; }
    public decimal? SecondDistinctScore { get; set; }
    public int DistinctClusterCount { get; set; }
}
