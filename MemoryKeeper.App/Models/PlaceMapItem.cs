using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.App.Models;

public sealed class PlaceMapItem
{
    public PlaceMapItem(MemorySearchResult result, double latitude, double longitude)
    {
        Result = result;
        Latitude = latitude;
        Longitude = longitude;
    }

    public MemorySearchResult Result { get; }

    public Guid PlaceId => Result.PlaceId;

    public string PlaceName => Result.PlaceName;

    public string RegionSummary =>
        string.Join(" / ", new[] { Result.Country, Result.City }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public int VisitRecordCount => Result.VisitRecordCount;

    public int PhotoCount => Result.PhotoCount;

    public bool HasFavorite => Result.HasFavorite;

    public Guid? RepresentativeMediaId => Result.RepresentativeMediaId;

    public string LastVisitText => Result.LastCapturedDate?.ToLocalTime().ToString("yyyy-MM-dd") ?? "-";

    public double Latitude { get; }

    public double Longitude { get; }

    public string MarkerInfo =>
        $"{PlaceName}<br/>방문 {VisitRecordCount}회 · 사진 {PhotoCount}<br/>최근 방문 {LastVisitText}";
}
