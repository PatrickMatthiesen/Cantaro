using Cantaro.Api.Models;

namespace Cantaro.Api.Services;

internal static class TrackVersionClassifier
{
    public static TrackVersionFlags Infer(ParsedTrackMetadata metadata)
    {
        var flags = TrackVersionFlags.None;

        foreach (var marker in metadata.VersionMarkers)
        {
            flags |= marker switch
            {
                "acoustic" or "stripped" => TrackVersionFlags.Acoustic,
                "live" => TrackVersionFlags.Live,
                "instrumental" => TrackVersionFlags.Instrumental,
                "orchestral" => TrackVersionFlags.Orchestral,
                "remix" or "vip-mix" or "club-mix" => TrackVersionFlags.Remix,
                "radio-edit" => TrackVersionFlags.RadioEdit,
                "extended-mix" => TrackVersionFlags.Extended,
                "demo" => TrackVersionFlags.Demo,
                "acapella" => TrackVersionFlags.ACappella,
                "karaoke" => TrackVersionFlags.Karaoke,
                "cover" => TrackVersionFlags.Cover,
                "remaster" => TrackVersionFlags.Remastered,
                "re-recorded" => TrackVersionFlags.ReRecorded,
                "clean" => TrackVersionFlags.Clean,
                "dirty" or "explicit" => TrackVersionFlags.Explicit,
                "edit" => TrackVersionFlags.AlternateTake,
                _ => TrackVersionFlags.None
            };
        }

        foreach (var modifier in metadata.PlaybackModifiers)
        {
            flags |= modifier switch
            {
                "speed up" or "sped up" or "nightcore" => TrackVersionFlags.SpedUp,
                "slowed" or "slowed + reverb" => TrackVersionFlags.Slowed,
                _ => TrackVersionFlags.None
            };
        }

        return flags;
    }
}
