namespace MemoryKeeper.App.Services;

/// <summary>
/// Seeds Place Management create mode (e.g. from Pending Memory GPS).
/// </summary>
public interface IPlaceEditorSeedState
{
    double? SeedLatitude { get; set; }

    double? SeedLongitude { get; set; }

    IReadOnlyList<Guid> SeedMediaIds { get; set; }

    bool TryConsumeSeed(out double latitude, out double longitude, out IReadOnlyList<Guid> mediaIds);
}
