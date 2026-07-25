namespace Cantaro.Api.Models;

public enum SongCreditRole : short
{
    Composer,
    Lyricist,
    Writer
}

/// <summary>
/// Ordered authorship credit for the underlying composition.
/// </summary>
public sealed class SongCredit
{
    public Guid Id { get; set; }
    public Guid SongId { get; set; }
    public Guid ArtistId { get; set; }
    public SongCreditRole Role { get; set; }
    public int Position { get; set; }
    public required string CreditedName { get; set; }

    public Song? Song { get; set; }
    public Artist? Artist { get; set; }
}
