namespace MemoryKeeper.Application.DTOs;

public sealed class PendingMemoryItemDto
{
    public string BackendFileId { get; init; } = string.Empty;

    public Guid MediaId { get; init; }

    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Absolute library file path used by the App thumbnail cache.
    /// </summary>
    public string AbsoluteLibraryPath { get; init; } = string.Empty;

    public DateTimeOffset? CapturedAt { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string Country { get; init; } = string.Empty;

    public string Province { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string District { get; init; } = string.Empty;

    public string RawPlaceName { get; init; } = string.Empty;

    public Guid? MemorykeeperPlaceId { get; init; }

    public int PlaceRevision { get; init; }

    public Guid? SuggestedPlaceId { get; init; }

    public string SuggestedPlaceName { get; init; } = string.Empty;

    public bool HasGps => Latitude is not null && Longitude is not null;

    public string GpsStatusText => HasGps ? "📍 GPS 있음" : "❌ GPS 없음";

    public string PlaceStatusText => "장소 정리 필요";

    public string GeographyText => string.Join(
        " ",
        new[] { Country, Province, City, District, RawPlaceName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Creates a UI display projection whose registered Place geography is authoritative.
    /// The raw cleanup values remain per-field fallbacks and the source DTO is not mutated.
    /// </summary>
    public PendingMemoryItemDto WithEffectiveGeography(PlaceDto? registeredPlace)
    {
        if (registeredPlace is null)
        {
            return this;
        }

        return new PendingMemoryItemDto
        {
            BackendFileId = BackendFileId,
            MediaId = MediaId,
            FileName = FileName,
            AbsoluteLibraryPath = AbsoluteLibraryPath,
            CapturedAt = CapturedAt,
            Latitude = Latitude,
            Longitude = Longitude,
            Country = FirstNotBlank(registeredPlace.Country, Country),
            Province = FirstNotBlank(
                registeredPlace.City,
                registeredPlace.Province,
                City,
                Province),
            City = FirstNotBlank(registeredPlace.City, City),
            District = FirstNotBlank(registeredPlace.District, District),
            RawPlaceName = FirstNotBlank(registeredPlace.DisplayName, RawPlaceName),
            MemorykeeperPlaceId = MemorykeeperPlaceId,
            PlaceRevision = PlaceRevision,
            SuggestedPlaceId = SuggestedPlaceId,
            SuggestedPlaceName = SuggestedPlaceName,
        };
    }

    private static string FirstNotBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
