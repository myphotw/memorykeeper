using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.DTOs;

public sealed class BackendChangeFeedDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<BackendChangeEventDto> Items { get; init; } = [];

    [JsonPropertyName("next_cursor")]
    public long NextCursor { get; init; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; init; }
}

public sealed class BackendChangeEventDto
{
    [JsonPropertyName("cursor")]
    public long Cursor { get; init; }

    [JsonPropertyName("service_name")]
    public string ServiceName { get; init; } = string.Empty;

    [JsonPropertyName("resource_type")]
    public string ResourceType { get; init; } = string.Empty;

    [JsonPropertyName("resource_id")]
    public string ResourceId { get; init; } = string.Empty;

    [JsonPropertyName("operation")]
    public string Operation { get; init; } = string.Empty;

    [JsonPropertyName("revision")]
    public int? Revision { get; init; }

    [JsonPropertyName("tombstone")]
    public bool Tombstone { get; init; }

    [JsonPropertyName("changed_at")]
    public DateTimeOffset ChangedAt { get; init; }
}
