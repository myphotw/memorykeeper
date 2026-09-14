using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs;

public static class MemoryKeeperPhotoCategories
{
    public const string Normal = "NORMAL";
    public const string Daily = "DAILY";

    public static bool IsSupported(string? value) =>
        string.Equals(value, Normal, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, Daily, StringComparison.OrdinalIgnoreCase);
}

public sealed class MemoryKeeperPhotoCategoryMutationRequest
{
    [JsonPropertyName("file_ids")]
    public IReadOnlyList<string> FileIds { get; init; } = [];

    [JsonPropertyName("photo_category")]
    public string PhotoCategory { get; init; } = MemoryKeeperPhotoCategories.Normal;

    [JsonPropertyName("expected_category_revisions")]
    public IReadOnlyDictionary<string, int> ExpectedCategoryRevisions { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

public sealed class MemoryKeeperPhotoCategoryMutationItemDto
{
    [JsonPropertyName("file_id")]
    public string FileId { get; init; } = string.Empty;

    [JsonPropertyName("photo_category")]
    public string PhotoCategory { get; init; } = MemoryKeeperPhotoCategories.Normal;

    [JsonPropertyName("category_revision")]
    public int CategoryRevision { get; init; }

    [JsonPropertyName("memorykeeper_place_id")]
    public Guid? MemorykeeperPlaceId { get; init; }

    [JsonPropertyName("place_match_source")]
    public string? PlaceMatchSource { get; init; }

    [JsonPropertyName("place_revision")]
    public int PlaceRevision { get; init; }
}

public sealed class MemoryKeeperPhotoCategoryMutationResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<MemoryKeeperPhotoCategoryMutationItemDto> Items { get; init; } = [];
}
