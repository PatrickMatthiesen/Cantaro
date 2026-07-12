namespace Cantaro.Api.Models;

public static class TrackSourcePresentationKinds
{
    public const string Audio = "audio";
    public const string MusicVideo = "music-video";
    public const string LyricVideo = "lyric-video";
    public const string CoverArtAudio = "cover-art-audio";
    public const string Visualizer = "visualizer";
    public const string LiveVideo = "live-video";

    public static IReadOnlyList<string> All { get; } =
    [
        Audio,
        MusicVideo,
        LyricVideo,
        CoverArtAudio,
        Visualizer,
        LiveVideo
    ];
}

public static class TrackSourceUploaderAuthorities
{
    public const string Official = "official";
    public const string User = "user";

    public static IReadOnlyList<string> All { get; } = [Official, User];
}
