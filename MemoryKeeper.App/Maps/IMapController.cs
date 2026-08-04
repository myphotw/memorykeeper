namespace MemoryKeeper.App.Maps;

/// <summary>
/// Map host abstraction kept in the App layer so the Google Maps provider can be swapped later.
/// </summary>
public interface IMapController
{
    bool IsReady { get; }

    event EventHandler? Ready;

    event EventHandler<Guid>? MarkerClicked;

    event EventHandler<Guid?>? MarkerHovered;

    event EventHandler<(double Lat, double Lng)>? MapClicked;

    event EventHandler<(double Lat, double Lng)>? EditableMarkerDragEnded;

    Task InitializeAsync(string? apiKey, CancellationToken cancellationToken = default);

    Task SetMarkersAsync(IReadOnlyList<MapMarker> markers, CancellationToken cancellationToken = default);

    Task SelectMarkerAsync(Guid? placeId, bool center = true, CancellationToken cancellationToken = default);

    Task HoverMarkerAsync(Guid? placeId, CancellationToken cancellationToken = default);

    Task HighlightMarkersAsync(IReadOnlyCollection<Guid> matchedPlaceIds, CancellationToken cancellationToken = default);

    Task CenterOnAsync(
        double latitude,
        double longitude,
        int? zoom = null,
        CancellationToken cancellationToken = default);

    Task SetZoomAsync(int zoom, CancellationToken cancellationToken = default);

    Task FitMarkersAsync(CancellationToken cancellationToken = default);

    Task ZoomByAsync(int delta, CancellationToken cancellationToken = default);

    Task EnableMapClickAsync(bool enabled, CancellationToken ct = default);

    Task SetEditablePinAsync(double lat, double lng, double radiusMeters, int zoom = 17, CancellationToken ct = default);

    Task UpdateEditableRadiusAsync(double radiusMeters, CancellationToken ct = default);

    Task ClearEditablePinAsync(CancellationToken ct = default);
}
