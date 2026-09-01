using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

/// <summary>Visit-record place year scoping helper (kept after VisitRecordQueryService removal).</summary>
public static class VisitRecordPlaceScoping
{
    private const int PreviewTake = 8;

    public static VisitRecordPlaceDto ScopeToYear(VisitRecordPlaceDto place, int year)
    {
        ArgumentNullException.ThrowIfNull(place);
        var yearPhotos = place.AllPhotos
            .Where(photo => photo.CaptureYear == year)
            .ToList();

        if (yearPhotos.Count == 0)
        {
            return place with
            {
                PhotoCount = 0,
                VisitRecordCount = 0,
                FavoriteCount = 0,
                CaptureYears = [year],
                AllPhotos = [],
                PreviewPhotos = [],
                RepresentativeMediaId = null,
                RepresentativeAbsolutePath = null
            };
        }

        var visitDates = place.VisitDates
            .Where(date => date.Year == year)
            .Distinct()
            .OrderBy(date => date)
            .ToList();
        var visitCount = visitDates.Count > 0
            ? CountDateRanges(visitDates)
            : yearPhotos
                .Where(photo => photo.CapturedAt.HasValue)
                .Select(photo => DateOnly.FromDateTime(photo.CapturedAt!.Value.Date))
                .Distinct()
                .Count();

        return place with
        {
            PhotoCount = yearPhotos.Count,
            VisitRecordCount = visitCount,
            FavoriteCount = yearPhotos.Count(photo => photo.IsFavorite),
            RepresentativeMediaId = yearPhotos[0].MediaId,
            RepresentativeAbsolutePath = yearPhotos[0].AbsoluteLibraryPath,
            FirstCapturedDate = visitDates.Count > 0
                ? ToDateOffset(visitDates[0])
                : yearPhotos.Min(photo => photo.CapturedAt),
            LastCapturedDate = visitDates.Count > 0
                ? ToDateOffset(visitDates[^1])
                : yearPhotos.Max(photo => photo.CapturedAt),
            VisitDates = visitDates,
            CaptureYears = [year],
            AllPhotos = yearPhotos,
            PreviewPhotos = yearPhotos.Take(PreviewTake).ToList(),
            MarkerScale = CalculateMarkerScale(visitCount, yearPhotos.Count)
        };
    }

    private static int CountDateRanges(IReadOnlyList<DateOnly> dates)
    {
        if (dates.Count == 0)
        {
            return 0;
        }

        var visits = 1;
        for (var index = 1; index < dates.Count; index++)
        {
            if (dates[index].DayNumber - dates[index - 1].DayNumber > 1)
            {
                visits++;
            }
        }

        return visits;
    }

    private static DateTimeOffset ToDateOffset(DateOnly date) =>
        new(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);

    public static double CalculateMarkerScale(int visitCount, int photoCount)
    {
        var score = Math.Max(1, visitCount * 2 + Math.Max(0, photoCount));
        var scale = 0.6 + Math.Log10(score + 1) * 0.45;
        return Math.Clamp(scale, 0.6, 1.7);
    }

    public static bool HasAnyPhotos(IEnumerable<VisitRecordPlaceDto> places) =>
        places.Any(place => place.PhotoCount > 0 || place.AllPhotos.Count > 0);

    public static bool CanDisplayMarker(VisitRecordPlaceDto place) =>
        PlaceIdentity.HasValidCoordinates(place.Latitude, place.Longitude);
}
