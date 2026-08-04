namespace MemoryKeeper.App.Maps;

public enum MapMarkerVisualState
{
    Default = 0,
    Matched = 1,
    Selected = 2
}

public sealed record MapMarker(
    Guid Id,
    string Title,
    double Latitude,
    double Longitude,
    string? Info = null,
    MapMarkerVisualState State = MapMarkerVisualState.Default,
    double Scale = 1.0,
    bool IsFavorite = false,
    bool IsMatched = false);
