using System.Text.Json;

namespace Cantaro.Api.Services;

public static class MediaProviderAvailabilitySnapshotCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<MediaProviderAvailabilityLink> links)
        => JsonSerializer.Serialize(links, SerializerOptions);

    public static IReadOnlyList<MediaProviderAvailabilityLink> Deserialize(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<MediaProviderAvailabilityLink>>(snapshot, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
