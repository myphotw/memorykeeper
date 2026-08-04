using System.Security.Cryptography;
using System.Text;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.App.Services;

/// <summary>
/// Adapts TC-Backend Gallery search/map/timeline/statistics into existing UI DTOs.
/// </summary>
public static class GalleryBackendBridge
{
    public static async Task<MemorySearchQueryResult> SearchPlacesAsync(
        IGalleryApiRepository galleryApi,
        string apiBaseUrl,
        int? year = null,
        string? country = null,
        string? province = null,
        string? city = null,
        string? district = null,
        string? place = null,
        bool? favorite = null,
        string? tag = null,
        string? keyword = null,
        string? serviceName = null,
        CancellationToken cancellationToken = default)
    {
        var page = await galleryApi.SearchAsync(
            year: year,
            country: country,
            city: city,
            tag: tag,
            favorite: favorite,
            serviceName: serviceName,
            keyword: keyword,
            page: 1,
            pageSize: 200,
            province: province,
            district: district,
            place: place,
            cancellationToken: cancellationToken);

        var items = GroupPhotosToSearchResults(page.Items, apiBaseUrl);
        var chips = new List<MemorySearchChipDto>();
        if (year is int y)
        {
            chips.Add(new MemorySearchChipDto { Label = $"{y}년", Kind = MemorySearchChipKind.Year });
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            chips.Add(new MemorySearchChipDto { Label = keyword.Trim(), Kind = MemorySearchChipKind.Place });
        }

        if (favorite == true)
        {
            chips.Add(new MemorySearchChipDto { Label = "즐겨찾기", Kind = MemorySearchChipKind.Favorite });
        }

        return new MemorySearchQueryResult
        {
            Items = items,
            Chips = chips,
            ResolvedRequest = new MemorySearchRequest
            {
                Year = year,
                SearchText = keyword,
            },
        };
    }

    public static async Task<VisitRecordQueryResult> QueryVisitRecordsAsync(
        IGalleryApiRepository galleryApi,
        string apiBaseUrl,
        string? keyword = null,
        int? year = null,
        string? country = null,
        CancellationToken cancellationToken = default)
    {
        var mapTask = galleryApi.GetMapAsync(year: year, cancellationToken: cancellationToken);
        var searchTask = galleryApi.SearchAsync(
            year: year,
            country: country,
            keyword: keyword,
            page: 1,
            pageSize: 200,
            cancellationToken: cancellationToken);

        await Task.WhenAll(mapTask, searchTask).ConfigureAwait(false);
        var map = await mapTask.ConfigureAwait(false);
        var search = await searchTask.ConfigureAwait(false);

        var mapPlaces = GroupMarkersToVisitPlaces(map.Items, apiBaseUrl);
        var timelinePlaces = GroupPhotosToVisitPlaces(search.Items, apiBaseUrl);

        return new VisitRecordQueryResult
        {
            AllMapPlaces = mapPlaces,
            TimelinePlaces = timelinePlaces.Count > 0 ? timelinePlaces : mapPlaces,
            Chips = string.IsNullOrWhiteSpace(keyword)
                ? []
                : [new MemorySearchChipDto { Label = keyword.Trim(), Kind = MemorySearchChipKind.Place }],
        };
    }

    public static async Task<IReadOnlyList<int>> GetTimelineYearsAsync(
        IGalleryApiRepository galleryApi,
        CancellationToken cancellationToken = default)
    {
        var timeline = await galleryApi.GetTimelineAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return timeline.Items
            .OrderByDescending(item => item.Year)
            .Select(item => item.Year)
            .ToList();
    }

    public static async Task<DashboardStatisticsDto> GetStatisticsAsync(
        IGalleryApiRepository galleryApi,
        CancellationToken cancellationToken = default)
    {
        var stats = await galleryApi.GetStatisticsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new DashboardStatisticsDto
        {
            PhotoCount = stats.TotalPhotos,
            PlaceCount = stats.ByCountry.Sum(x => x.Count) > 0
                ? stats.ByCountry.Count
                : stats.ByYear.Count,
            VisitRecordCount = stats.GpsCount,
            FavoriteCount = 0,
            TagCount = stats.AiTagCount,
        };
    }

    public static IReadOnlyList<MemorySearchResult> GroupPhotosToSearchResults(
        IReadOnlyList<PhotoDto> photos,
        string apiBaseUrl)
    {
        return photos
            .GroupBy(photo => PlaceKey(photo.Country, photo.City, photo.PlaceName))
            .Select(group =>
            {
                var first = group.First();
                var dates = group
                    .Select(p => p.CaptureDatetime)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .OrderBy(d => d)
                    .ToList();
                var rep = group.FirstOrDefault(p => p.Favorite) ?? first;
                return new MemorySearchResult
                {
                    PlaceId = StablePlaceId(first.Country, first.City, first.PlaceName),
                    PlaceName = string.IsNullOrWhiteSpace(first.PlaceName) ? "장소 미지정" : first.PlaceName!,
                    Country = first.Country ?? string.Empty,
                    City = first.City ?? string.Empty,
                    PhotoCount = group.Count(),
                    VisitRecordCount = dates.Select(d => d.Date).Distinct().Count(),
                    FavoriteCount = group.Count(p => p.Favorite),
                    RepresentativeMediaId = GalleryBackendMapper.ParseFileId(rep.FileId) is { } mid && mid != Guid.Empty
                        ? mid
                        : null,
                    FirstCapturedDate = dates.FirstOrDefault(),
                    LastCapturedDate = dates.LastOrDefault(),
                };
            })
            .OrderByDescending(item => item.LastCapturedDate)
            .ToList();
    }

    public static IReadOnlyList<VisitRecordPlaceDto> GroupMarkersToVisitPlaces(
        IReadOnlyList<MapMarkerDto> markers,
        string apiBaseUrl)
    {
        return markers
            .GroupBy(marker => PlaceKey(null, null, marker.PlaceName))
            .Select(group =>
            {
                var first = group.First();
                var years = group.Where(m => m.Year.HasValue).Select(m => m.Year!.Value).Distinct().OrderByDescending(y => y).ToList();
                var rep = first;
                var thumb = GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, rep.Thumbnail);
                var mediaId = GalleryBackendMapper.ParseFileId(rep.FileId);
                return new VisitRecordPlaceDto
                {
                    PlaceId = StablePlaceId(null, null, first.PlaceName),
                    PlaceName = string.IsNullOrWhiteSpace(first.PlaceName) ? "장소 미지정" : first.PlaceName!,
                    Country = string.Empty,
                    City = string.Empty,
                    Latitude = first.Latitude,
                    Longitude = first.Longitude,
                    PhotoCount = group.Count(),
                    VisitRecordCount = group.Count(),
                    FavoriteCount = 0,
                    RepresentativeMediaId = mediaId == Guid.Empty ? null : mediaId,
                    RepresentativeAbsolutePath = thumb,
                    CaptureYears = years,
                    FirstCapturedDate = years.Count > 0
                        ? new DateTimeOffset(years.Min(), 1, 1, 0, 0, 0, TimeSpan.Zero)
                        : null,
                    LastCapturedDate = years.Count > 0
                        ? new DateTimeOffset(years.Max(), 12, 31, 0, 0, 0, TimeSpan.Zero)
                        : null,
                    AllPhotos = group.Select(m => ToPreview(m, apiBaseUrl)).ToList(),
                    PreviewPhotos = group.Take(8).Select(m => ToPreview(m, apiBaseUrl)).ToList(),
                    MarkerScale = 1.0,
                    IsUnclassified = string.IsNullOrWhiteSpace(first.PlaceName),
                };
            })
            .OrderByDescending(p => p.PhotoCount)
            .ToList();
    }

    public static IReadOnlyList<VisitRecordPlaceDto> GroupPhotosToVisitPlaces(
        IReadOnlyList<PhotoDto> photos,
        string apiBaseUrl)
    {
        return photos
            .GroupBy(photo => PlaceKey(photo.Country, photo.City, photo.PlaceName))
            .Select(group =>
            {
                var first = group.First();
                var dates = group.Select(p => p.CaptureDatetime).Where(d => d.HasValue).Select(d => d!.Value).OrderBy(d => d).ToList();
                var years = dates.Select(d => d.Year).Distinct().OrderByDescending(y => y).ToList();
                var rep = group.FirstOrDefault(p => p.Favorite) ?? first;
                var mediaId = GalleryBackendMapper.ParseFileId(rep.FileId);
                var path = GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, rep.ThumbnailUrl)
                           ?? GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, rep.PreviewUrl);
                return new VisitRecordPlaceDto
                {
                    PlaceId = StablePlaceId(first.Country, first.City, first.PlaceName),
                    PlaceName = string.IsNullOrWhiteSpace(first.PlaceName) ? "장소 미지정" : first.PlaceName!,
                    Country = first.Country ?? string.Empty,
                    City = first.City ?? string.Empty,
                    Latitude = 0,
                    Longitude = 0,
                    PhotoCount = group.Count(),
                    VisitRecordCount = dates.Select(d => d.Date).Distinct().Count(),
                    FavoriteCount = group.Count(p => p.Favorite),
                    RepresentativeMediaId = mediaId == Guid.Empty ? null : mediaId,
                    RepresentativeAbsolutePath = path,
                    CaptureYears = years,
                    FirstCapturedDate = dates.FirstOrDefault(),
                    LastCapturedDate = dates.LastOrDefault(),
                    AllPhotos = group.Select(p => ToPreview(p, apiBaseUrl)).ToList(),
                    PreviewPhotos = group.Take(8).Select(p => ToPreview(p, apiBaseUrl)).ToList(),
                    MarkerScale = 1.0,
                    IsUnclassified = string.IsNullOrWhiteSpace(first.PlaceName),
                };
            })
            .OrderByDescending(p => p.LastCapturedDate)
            .ToList();
    }

    public static Guid StablePlaceId(string? country, string? city, string? placeName)
    {
        var key = PlaceKey(country, city, placeName);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var bytes = new byte[16];
        Buffer.BlockCopy(hash, 0, bytes, 0, 16);
        return new Guid(bytes);
    }

    private static string PlaceKey(string? country, string? city, string? placeName) =>
        $"{country?.Trim() ?? ""}|{city?.Trim() ?? ""}|{placeName?.Trim() ?? ""}".ToLowerInvariant();

    private static VisitRecordPreviewPhotoDto ToPreview(MapMarkerDto marker, string apiBaseUrl)
    {
        var id = GalleryBackendMapper.ParseFileId(marker.FileId);
        return new VisitRecordPreviewPhotoDto
        {
            MediaId = id,
            FileName = marker.FileId,
            AbsoluteLibraryPath = GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, marker.Thumbnail) ?? string.Empty,
            IsFavorite = false,
            CapturedAt = marker.Year is int y ? new DateTimeOffset(y, 1, 1, 0, 0, 0, TimeSpan.Zero) : null,
            CaptureYear = marker.Year ?? 0,
        };
    }

    private static VisitRecordPreviewPhotoDto ToPreview(PhotoDto photo, string apiBaseUrl)
    {
        var id = GalleryBackendMapper.ParseFileId(photo.FileId);
        return new VisitRecordPreviewPhotoDto
        {
            MediaId = id,
            FileName = photo.Filename,
            AbsoluteLibraryPath = GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, photo.ThumbnailUrl)
                                  ?? GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, photo.PreviewUrl)
                                  ?? string.Empty,
            IsFavorite = photo.Favorite,
            CapturedAt = photo.CaptureDatetime,
            CaptureYear = photo.CaptureDatetime?.Year ?? 0,
        };
    }
}
