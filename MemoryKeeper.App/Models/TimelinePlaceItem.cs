using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.App.Models;

public sealed class TimelinePlaceItem
{
    public TimelinePlaceItem(MemorySearchResult result)
    {
        Result = result;
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

    public string FirstVisitText => FormatDate(Result.FirstCapturedDate);

    public string LastVisitText => FormatDate(Result.LastCapturedDate);

    private static string FormatDate(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("yyyy-MM-dd") ?? "-";
    }
}
