using System.Text.Json.Serialization;

namespace Cantaro.Api.Services;

public sealed class MyAnimeListPage<T>
{
    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = [];

    [JsonPropertyName("paging")]
    public MyAnimeListPaging? Paging { get; set; }
}

public sealed class MyAnimeListPaging
{
    [JsonPropertyName("next")]
    public string? Next { get; set; }
}

public sealed class MyAnimeListSearchEdge
{
    [JsonPropertyName("node")]
    public MyAnimeListMedia? Node { get; set; }
}

public sealed class MyAnimeListAnimeListEdge
{
    [JsonPropertyName("node")]
    public MyAnimeListMedia? Node { get; set; }

    [JsonPropertyName("list_status")]
    public MyAnimeListAnimeListStatus? ListStatus { get; set; }
}

public sealed class MyAnimeListMangaListEdge
{
    [JsonPropertyName("node")]
    public MyAnimeListMedia? Node { get; set; }

    [JsonPropertyName("list_status")]
    public MyAnimeListMangaListStatus? ListStatus { get; set; }
}

public sealed class MyAnimeListUser
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class MyAnimeListMedia
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("main_picture")]
    public MyAnimeListPicture? MainPicture { get; set; }

    [JsonPropertyName("alternative_titles")]
    public MyAnimeListAlternativeTitles? AlternativeTitles { get; set; }

    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    [JsonPropertyName("synopsis")]
    public string? Synopsis { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("num_episodes")]
    public int NumEpisodes { get; set; }

    [JsonPropertyName("num_chapters")]
    public int NumChapters { get; set; }

    [JsonPropertyName("num_volumes")]
    public int NumVolumes { get; set; }
}

public sealed class MyAnimeListPicture
{
    [JsonPropertyName("medium")]
    public string? Medium { get; set; }

    [JsonPropertyName("large")]
    public string? Large { get; set; }
}

public sealed class MyAnimeListAlternativeTitles
{
    [JsonPropertyName("synonyms")]
    public List<string>? Synonyms { get; set; }

    [JsonPropertyName("en")]
    public string? English { get; set; }

    [JsonPropertyName("ja")]
    public string? Japanese { get; set; }
}

public sealed class MyAnimeListAnimeListStatus
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("num_episodes_watched")]
    public int NumEpisodesWatched { get; set; }

    [JsonPropertyName("is_rewatching")]
    public bool IsRewatching { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class MyAnimeListMangaListStatus
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("num_chapters_read")]
    public int NumChaptersRead { get; set; }

    [JsonPropertyName("num_volumes_read")]
    public int NumVolumesRead { get; set; }

    [JsonPropertyName("is_rereading")]
    public bool IsRereading { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}
