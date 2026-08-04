namespace MemoryKeeper.Application.DTOs;

public sealed class PendingMemoryItemDto
{
    public Guid MediaId { get; init; }

    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Absolute library file path used by the App thumbnail cache.
    /// </summary>
    public string AbsoluteLibraryPath { get; init; } = string.Empty;

    public DateTimeOffset? CapturedAt { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public bool HasGps => Latitude is not null && Longitude is not null;

    public string GpsStatusText => HasGps ? "📍 GPS 있음" : "❌ GPS 없음";

    public string PlaceStatusText => "장소 미등록";
}
