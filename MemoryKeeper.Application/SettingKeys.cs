namespace MemoryKeeper.Application;

public static class SettingKeys
{
    /// <summary>
    /// Legacy read-only key retained so existing installations and map rendering do not fail.
    /// New user flows must not request or write this value.
    /// </summary>
    public const string GoogleMapsApiKey = "GoogleMaps:ApiKey";

    public const string PlaceDefaultRadiusMeters = "Place:DefaultRadiusMeters";

    /// <summary>
    /// JSON array of recent Tag Ids (most recent first). Max 10.
    /// </summary>
    public const string RecentTagIds = "Tag:RecentTagIds";

    /// <summary>
    /// JSON array of recent memory search queries (most recent first). Max 10.
    /// </summary>
    public const string RecentSearchQueries = "Search:RecentQueries";

    public const string TravelHomeLatitude = "Travel:HomeLatitude";

    public const string TravelHomeLongitude = "Travel:HomeLongitude";

    public const string TravelHomeAddress = "Travel:HomeAddress";

    /// <summary>
    /// Provider place ID for the configured home location (optional).
    /// </summary>
    public const string TravelHomePlaceId = "Travel:HomePlaceId";

    /// <summary>
    /// "true" when first-run setup wizard has completed.
    /// </summary>
    public const string SetupCompleted = "App:SetupCompleted";

    /// <summary>
    /// Photo Detail Panel width in DIPs (MK-042S).
    /// </summary>
    public const string PhotoDetailPanelWidth = "UI:PhotoDetailPanelWidth";

    /// <summary>
    /// Default editable map pin radius in meters (MK-042S).
    /// </summary>
    public const string MapPickDefaultRadiusMeters = "Place:MapPickDefaultRadiusMeters";
}
