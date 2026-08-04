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

        var visitDates = yearPhotos
            .Select(photo => (photo.CapturedAt ?? default).Date)
            .Distinct()
            .Count();

        return place with
        {
            PhotoCount = yearPhotos.Count,
            VisitRecordCount = visitDates,
            FavoriteCount = yearPhotos.Count(photo => photo.IsFavorite),
            RepresentativeMediaId = yearPhotos[0].MediaId,
            RepresentativeAbsolutePath = yearPhotos[0].AbsoluteLibraryPath,
            FirstCapturedDate = yearPhotos.Min(photo => photo.CapturedAt),
            LastCapturedDate = yearPhotos.Max(photo => photo.CapturedAt),
            CaptureYears = [year],
            AllPhotos = yearPhotos,
            PreviewPhotos = yearPhotos.Take(PreviewTake).ToList(),
            MarkerScale = CalculateMarkerScale(visitDates, yearPhotos.Count)
        };
    }

    public static double CalculateMarkerScale(int visitCount, int photoCount)
    {
        var score = Math.Max(1, visitCount * 2 + Math.Max(0, photoCount));
        var scale = 0.6 + Math.Log10(score + 1) * 0.45;
        return Math.Clamp(scale, 0.6, 1.7);
    }
}
