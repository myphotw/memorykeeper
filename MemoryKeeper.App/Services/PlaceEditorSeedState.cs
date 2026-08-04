namespace MemoryKeeper.App.Services;

public sealed class PlaceEditorSeedState : IPlaceEditorSeedState
{
    private static readonly (double Lat, double Lng) DefaultCenter = (37.5665, 126.9780);

    public double? SeedLatitude { get; set; }

    public double? SeedLongitude { get; set; }

    public IReadOnlyList<Guid> SeedMediaIds { get; set; } = [];

    public bool TryConsumeSeed(out double latitude, out double longitude, out IReadOnlyList<Guid> mediaIds)
    {
        var hasMedia = SeedMediaIds.Count > 0;
        var hasCoords = SeedLatitude is not null && SeedLongitude is not null;
        if (!hasMedia && !hasCoords)
        {
            latitude = 0;
            longitude = 0;
            mediaIds = [];
            return false;
        }

        latitude = SeedLatitude ?? DefaultCenter.Lat;
        longitude = SeedLongitude ?? DefaultCenter.Lng;
        mediaIds = SeedMediaIds.Count > 0 ? SeedMediaIds.ToList() : [];
        SeedLatitude = null;
        SeedLongitude = null;
        SeedMediaIds = [];
        return true;
    }
}
