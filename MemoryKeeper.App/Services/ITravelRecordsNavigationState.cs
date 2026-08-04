using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.App.Services;

public interface ITravelRecordsNavigationState
{
    TravelRecordsDetailKind? PendingDetailKind { get; set; }

    TravelSeason? PendingDetailSeason { get; set; }

    /// <summary>Home Hero 등에서 넘어온 회상 포커스 장소.</summary>
    Guid? PendingFocusPlaceId { get; set; }

    int? PendingFocusYear { get; set; }

    string? PendingFocusPlaceName { get; set; }

    Guid? PendingFocusMediaId { get; set; }

    void RequestDetail(TravelRecordsDetailKind kind, TravelSeason? season = null);

    void RequestMemoryFocus(
        Guid placeId,
        int? year = null,
        string? placeName = null,
        Guid? mediaId = null);
}

public sealed class TravelRecordsNavigationState : ITravelRecordsNavigationState
{
    public TravelRecordsDetailKind? PendingDetailKind { get; set; }

    public TravelSeason? PendingDetailSeason { get; set; }

    public Guid? PendingFocusPlaceId { get; set; }

    public int? PendingFocusYear { get; set; }

    public string? PendingFocusPlaceName { get; set; }

    public Guid? PendingFocusMediaId { get; set; }

    public void RequestDetail(TravelRecordsDetailKind kind, TravelSeason? season = null)
    {
        PendingDetailKind = kind;
        PendingDetailSeason = season;
    }

    public void RequestMemoryFocus(
        Guid placeId,
        int? year = null,
        string? placeName = null,
        Guid? mediaId = null)
    {
        PendingFocusPlaceId = placeId;
        PendingFocusYear = year is > 0 ? year : null;
        PendingFocusPlaceName = string.IsNullOrWhiteSpace(placeName) ? null : placeName.Trim();
        PendingFocusMediaId = mediaId;
    }
}
