namespace Cantaro.Api.Models;

[Flags]
public enum TrackVersionFlags : long
{
    None = 0,
    Acoustic = 1L << 0,
    Live = 1L << 1,
    Instrumental = 1L << 2,
    Orchestral = 1L << 3,
    Remix = 1L << 4,
    RadioEdit = 1L << 5,
    Extended = 1L << 6,
    Demo = 1L << 7,
    ACappella = 1L << 8,
    Karaoke = 1L << 9,
    Cover = 1L << 10,
    Remastered = 1L << 11,
    ReRecorded = 1L << 12,
    Clean = 1L << 13,
    Explicit = 1L << 14,
    Slowed = 1L << 15,
    SpedUp = 1L << 16,
    AlternateTake = 1L << 17,
    Medley = 1L << 18,
    Mashup = 1L << 19
}
