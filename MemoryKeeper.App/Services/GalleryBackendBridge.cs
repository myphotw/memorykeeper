using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.App.Services;

/// <summary>
/// Adapts TC-Backend Gallery search/map/timeline/statistics into existing UI DTOs.
/// </summary>
public static class GalleryBackendBridge
{
    /// <summary>Fast Gallery home read: one first page plus summary, never a complete catalog snapshot.</summary>
    public static async Task<HomeDashboardDto> GetFastHomeDashboardAsync(
        IFastGalleryApiRepository fastGallery,
        string apiBaseUrl,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var pageTask = fastGallery.GetPhotosAsync(new FastGalleryPhotoQuery { Limit = 50 }, cancellationToken);
        var summaryTask = fastGallery.GetSummaryAsync(cancellationToken);
        var page = await pageTask.ConfigureAwait(false);
        FastGallerySummaryDto summary;
        try
        {
            summary = await summaryTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A summary outage must not hide the immediately useful first photo page.
            summary = new FastGallerySummaryDto();
        }
        var photos = page.Items;
        var dashboardPhotos = photos.Take(6).Select(photo =>
        {
            var thumbnail = ResolveThumbnailUrl(apiBaseUrl, photo.FileId, photo.ThumbnailUrl);
            var preview = BackendMediaUrlResolver.ToAbsoluteUrl(apiBaseUrl, photo.PreviewUrl);
            logger?.LogDebug(
                "Fast media mapped. Surface=HomeRecent FileId={FileId} Thumbnail={Thumbnail} Preview={Preview} Candidate={Candidate}",
                photo.FileId,
                BackendMediaUrlResolver.DescribeForDiagnostics(apiBaseUrl, photo.ThumbnailUrl),
                BackendMediaUrlResolver.DescribeForDiagnostics(apiBaseUrl, photo.PreviewUrl),
                BackendMediaUrlResolver.DescribeForDiagnostics(apiBaseUrl, thumbnail ?? preview));
            return new DashboardPhotoDto
            {
                MediaId = GalleryBackendMapper.ParseFileId(photo.FileId),
                FileName = photo.Filename,
                IsFavorite = photo.Favorite,
                PlaceName = photo.PlaceDisplayName,
                Country = photo.Country,
                CapturedAt = photo.EffectiveCaptureDatetime,
                AbsoluteLibraryPath = thumbnail ?? preview ?? string.Empty,
                FallbackAbsoluteLibraryPath = preview,
            };
        }).Where(photo => photo.MediaId != Guid.Empty).ToList();
        var recentVisits = photos.Where(photo => photo.MemorykeeperPlaceId.HasValue)
                .GroupBy(photo => photo.MemorykeeperPlaceId!.Value)
                .Select(group =>
                {
                    var rep = group.First();
                    return new RecentVisitDto
                    {
                        PlaceId = group.Key,
                        PlaceName = rep.PlaceDisplayName ?? "장소",
                        Country = rep.Country ?? string.Empty,
                        PhotoCount = group.Count(),
                        VisitRecordCount = group.Select(item => item.EffectiveCaptureDate).Distinct().Count(),
                        LastVisitDate = group.Max(item => item.EffectiveCaptureDatetime),
                        RepresentativeMediaId = GalleryBackendMapper.ParseFileId(rep.FileId),
                        AbsoluteLibraryPath = ResolveThumbnailUrl(apiBaseUrl, rep.FileId, rep.ThumbnailUrl)
                                               ?? GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, rep.PreviewUrl),
                        FallbackAbsoluteLibraryPath = BackendMediaUrlResolver.ToAbsoluteUrl(apiBaseUrl, rep.PreviewUrl),
                    };
                }).OrderByDescending(item => item.LastVisitDate).Take(3).ToList();
        var heroes = recentVisits.Take(3).Select(visit => new HeroMemoryDto
        {
            PlaceId = visit.PlaceId, PlaceName = visit.PlaceName,
            Year = visit.LastVisitDate?.Year ?? 0, PhotoCount = visit.PhotoCount,
            VisitRecordCount = visit.VisitRecordCount, RepresentativeMediaId = visit.RepresentativeMediaId,
            AbsoluteLibraryPath = visit.AbsoluteLibraryPath, KindLabel = "최근 방문",
            FallbackAbsoluteLibraryPath = visit.FallbackAbsoluteLibraryPath,
            DateText = visit.LastVisitDate?.ToLocalTime().ToString("yyyy.MM.dd") ?? string.Empty,
        }).ToList();
        return new HomeDashboardDto
        {
            HeroMemories = heroes,
            RecentVisits = recentVisits,
            RecentImports = dashboardPhotos,
            Favorites = dashboardPhotos.Where(photo => photo.IsFavorite).ToList(),
            Statistics = new DashboardStatisticsDto
            {
                PhotoCount = summary.TotalPhotos, FavoriteCount = summary.FavoriteCount, GpsCount = summary.GpsCount,
                PlaceCount = 0, CountryCount = summary.ByCountry.Count,
                ByYear = summary.ByYear.Select(item => new DashboardStatBucketDto { Name = item.Name, Count = item.Count }).ToList(),
                ByCountry = summary.ByCountry.Select(item => new DashboardStatBucketDto { Name = item.Name, Count = item.Count }).ToList(),
                LastUpdatedText = summary.EffectiveDateMax?.ToString("yyyy.MM.dd") ?? string.Empty,
            },
        };
    }

    /// <summary>
    /// Applies the authoritative Travel aggregate after the Fast Gallery home shell is visible.
    /// This deliberately does not perform I/O so callers can preserve first-paint responsiveness.
    /// </summary>
    public static HomeDashboardDto ApplyAuthoritativePlaceAggregates(
        HomeDashboardDto dashboard,
        IReadOnlyList<TravelPlaceAggregateRaw> placeAggregates) =>
        HomeDashboardProjection.ApplyAuthoritativePlaceAggregates(dashboard, placeAggregates);

    private static DateTimeOffset ToLocalDateOffset(DateTime value)
    {
        var local = DateTime.SpecifyKind(value, DateTimeKind.Local);
        return new DateTimeOffset(local);
    }
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
        IGalleryPhotoCatalog catalog,
        string? keyword = null,
        int? year = null,
        string? country = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await catalog.QueryAsync(year, country, keyword, cancellationToken)
            .ConfigureAwait(false);
        // Search rows enrich map coordinates with the same country/place metadata and
        // authenticated thumbnail URLs used by Gallery and Travel Records.
        var enrichedPlaces = GroupPhotosToVisitPlaces(
            snapshot.Photos,
            snapshot.ApiBaseUrl,
            snapshot.MapMarkers,
            snapshot.LocationMetadataByFileId);
        var markerFallback = GroupMarkersToVisitPlaces(snapshot.MapMarkers, snapshot.ApiBaseUrl);
        var mapPlaces = enrichedPlaces.Count > 0 ? enrichedPlaces : markerFallback;

        return new VisitRecordQueryResult
        {
            AllMapPlaces = mapPlaces,
            TimelinePlaces = mapPlaces,
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

        return MapStatistics(stats, placeCount: 0);
    }

    /// <summary>
    /// Home dashboard from existing Gallery search/map/statistics APIs (no Backend changes).
    /// </summary>
    public static async Task<HomeDashboardDto> GetHomeDashboardAsync(
        IGalleryApiRepository galleryApi,
        IGalleryPhotoCatalog catalog,
        GalleryHierarchyService hierarchyService,
        CancellationToken cancellationToken = default)
    {
        var statsTask = galleryApi.GetStatisticsAsync(cancellationToken: cancellationToken);
        var catalogTask = catalog.QueryAsync(cancellationToken: cancellationToken);
        var visitsTask = hierarchyService.QueryVisitRecordsAsync(
            new GalleryHierarchyQuery(), cancellationToken);

        await Task.WhenAll(statsTask, catalogTask, visitsTask).ConfigureAwait(false);
        var stats = await statsTask.ConfigureAwait(false);
        var snapshot = await catalogTask.ConfigureAwait(false);
        var sharedVisits = await visitsTask.ConfigureAwait(false);
        var photos = snapshot.Photos;
        var apiBaseUrl = snapshot.ApiBaseUrl;

        var placePool = sharedVisits.AllMapPlaces;
        var recentVisits = placePool
            .Where(p => !p.IsUnclassified)
            .OrderByDescending(p => p.LastCapturedDate ?? DateTimeOffset.MinValue)
            .Take(3)
            .Select(p => new RecentVisitDto
            {
                PlaceId = p.PlaceId,
                PlaceName = p.PlaceName,
                Country = p.Country ?? string.Empty,
                AbsoluteLibraryPath = p.RepresentativeAbsolutePath,
                RepresentativeMediaId = p.RepresentativeMediaId,
                VisitRecordCount = p.VisitRecordCount,
                PhotoCount = p.PhotoCount,
                LastVisitDate = p.LastCapturedDate,
                TopTags = p.TopTags,
            })
            .ToList();

        var photosById = photos
            .Where(photo => !string.IsNullOrWhiteSpace(photo.FileId))
            .GroupBy(photo => photo.FileId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var recentPhotos = snapshot.RecentPhotoFileIds
            .Where(photosById.ContainsKey)
            .Select(fileId => photosById[fileId])
            .Take(6)
            .ToList();
        var recentImports = recentPhotos
            .Select(photo => ToDashboardPhoto(photo, apiBaseUrl))
            .Where(photo => photo.MediaId != Guid.Empty)
            .ToList();
        var favorites = photos
            .Where(photo => photo.Favorite)
            .OrderByDescending(photo => photo.CaptureDatetime)
            .Take(6)
            .Select(photo => ToDashboardPhoto(photo, apiBaseUrl))
            .Where(photo => photo.MediaId != Guid.Empty)
            .ToList();
        var pendingPhotos = photos
            .Where(photo => !photo.MemorykeeperPlaceId.HasValue)
            .ToList();
        var representativePending = recentPhotos.FirstOrDefault(pendingPhotos.Contains)
                                    ?? pendingPhotos.OrderByDescending(photo => photo.CaptureDatetime).FirstOrDefault();
        var pendingSummary = new PendingSummaryDto
        {
            Total = pendingPhotos.Count,
            NoGps = pendingPhotos.Count(photo => !photo.HasGps),
            HasGps = pendingPhotos.Count(photo => photo.HasGps),
            UnknownDate = pendingPhotos.Count(photo => !photo.CaptureDatetime.HasValue),
            RepresentativeMediaId = representativePending is null
                ? null
                : GalleryBackendMapper.ParseFileId(representativePending.FileId),
            RepresentativeAbsoluteLibraryPath = representativePending is null
                ? null
                : ResolveThumbnailUrl(apiBaseUrl, representativePending.FileId, representativePending.ThumbnailUrl)
                  ?? GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, representativePending.PreviewUrl),
            LatestImportedAt = representativePending?.ImportedAt ?? representativePending?.CreatedAt,
        };
        var today = DateTimeOffset.Now;
        var todayMemories = photos
            .Where(photo => photo.CaptureDatetime is DateTimeOffset captured
                            && captured.ToLocalTime().Month == today.Month
                            && captured.ToLocalTime().Day == today.Day
                            && captured.ToLocalTime().Year < today.Year)
            .OrderByDescending(photo => photo.CaptureDatetime)
            .Take(8)
            .Select(photo => new TodayMemoryPhotoDto
            {
                MediaId = GalleryBackendMapper.ParseFileId(photo.FileId),
                PlaceId = photo.MemorykeeperPlaceId,
                PlaceName = FirstNonEmpty(photo.PlaceDisplayName, photo.PlaceName) ?? string.Empty,
                AbsoluteLibraryPath = ResolveThumbnailUrl(apiBaseUrl, photo.FileId, photo.ThumbnailUrl)
                                      ?? GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, photo.PreviewUrl)
                                      ?? string.Empty,
                YearsAgo = today.Year - photo.CaptureDatetime!.Value.ToLocalTime().Year,
            })
            .Where(photo => photo.MediaId != Guid.Empty)
            .ToList();

        var heroes = new List<HeroMemoryDto>();
        foreach (var visit in recentVisits)
        {
            var locationLine = string.Join(
                " ",
                new[] { visit.Country, visit.PlaceName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            heroes.Add(new HeroMemoryDto
            {
                PlaceId = visit.PlaceId,
                PlaceName = visit.PlaceName,
                Year = visit.LastVisitDate?.Year ?? 0,
                PhotoCount = visit.PhotoCount,
                VisitRecordCount = visit.VisitRecordCount,
                RepresentativeMediaId = visit.RepresentativeMediaId,
                AbsoluteLibraryPath = visit.AbsoluteLibraryPath,
                TopTags = visit.TopTags,
                KindLabel = "최근 방문",
                DateText = visit.LastVisitDate?.ToLocalTime().ToString("yyyy.MM.dd") ?? string.Empty,
                Description = string.IsNullOrWhiteSpace(locationLine)
                    ? "오늘도 소중한 추억을 만나보세요."
                    : locationLine,
            });
        }

        if (heroes.Count == 0)
        {
            foreach (var photo in photos.Take(5))
            {
                var thumb = ResolveThumbnailUrl(apiBaseUrl, photo.FileId, photo.ThumbnailUrl)
                            ?? GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, photo.PreviewUrl);
                var mediaId = GalleryBackendMapper.ParseFileId(photo.FileId);
                if (mediaId == Guid.Empty)
                {
                    continue;
                }

                var locationLine = string.Join(
                    " ",
                    new[] { photo.Country, photo.City, photo.PlaceName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                heroes.Add(new HeroMemoryDto
                {
                    PlaceId = PlaceIdentity.MapStableId(photo.PlaceName),
                    PlaceName = string.IsNullOrWhiteSpace(photo.PlaceName) ? "추억" : photo.PlaceName!,
                    Year = photo.CaptureDatetime?.Year ?? 0,
                    PhotoCount = 1,
                    RepresentativeMediaId = mediaId,
                    AbsoluteLibraryPath = thumb,
                    KindLabel = "최근 사진",
                    DateText = photo.CaptureDatetime?.ToLocalTime().ToString("yyyy.MM.dd") ?? string.Empty,
                    Description = string.IsNullOrWhiteSpace(locationLine)
                        ? "오늘도 소중한 추억을 만나보세요."
                        : locationLine,
                });
            }
        }

        var placeCount = placePool.Count;
        var lastDate = photos
            .Select(p => p.CaptureDatetime)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .DefaultIfEmpty()
            .Max();

        return new HomeDashboardDto
        {
            HeroMemories = heroes,
            RecentVisits = recentVisits,
            RecentImports = recentImports,
            Statistics = MapStatistics(
                stats,
                placeCount,
                lastDate,
                photos.Count(photo => photo.Favorite)),
            PendingSummary = pendingSummary,
            TodayMemories = todayMemories,
            Favorites = favorites,
            RecentQueries = [],
        };
    }

    /// <summary>
    /// Visit Map read path backed by the marker endpoint and Travel place aggregates.
    /// It never downloads the paged Gallery photo catalog.
    /// </summary>
    public static async Task<VisitRecordQueryResult> QueryFastVisitRecordsAsync(
        IGalleryApiRepository galleryApi,
        ITravelRecordsRepository travelRecords,
        string apiBaseUrl,
        GalleryHierarchyQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var mapTask = galleryApi.GetMapAsync(query.Year, cancellationToken: cancellationToken);
        var aggregatesTask = travelRecords.GetPlaceAggregatesAsync(cancellationToken);
        var map = await mapTask.ConfigureAwait(false);

        IReadOnlyList<TravelPlaceAggregateRaw> aggregates;
        try
        {
            aggregates = await aggregatesTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Marker-only Visit Map remains usable during a Travel aggregate outage.
            aggregates = [];
        }

        var aggregatesById = aggregates
            .Where(item => item.PlaceId != Guid.Empty)
            .GroupBy(item => item.PlaceId)
            .ToDictionary(group => group.Key, group => group.First());

        var places = map.Items
            .GroupBy(MarkerPlaceId)
            .Select(group => BuildFastVisitPlace(group.Key, group.ToList(), aggregatesById, apiBaseUrl, query))
            .Where(place => MatchesVisitQuery(place, query))
            .OrderByDescending(place => place.LastCapturedDate)
            .ThenBy(place => place.PlaceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chips = new List<MemorySearchChipDto>();
        if (query.Year is int year)
        {
            chips.Add(new MemorySearchChipDto { Label = $"{year}년", Kind = MemorySearchChipKind.Year });
        }

        if (!string.IsNullOrWhiteSpace(query.Country))
        {
            chips.Add(new MemorySearchChipDto { Label = query.Country.Trim(), Kind = MemorySearchChipKind.Place });
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            chips.Add(new MemorySearchChipDto { Label = query.SearchText.Trim(), Kind = MemorySearchChipKind.Place });
        }

        return new VisitRecordQueryResult
        {
            AllMapPlaces = places,
            TimelinePlaces = places,
            Chips = chips,
        };
    }

    private static DashboardPhotoDto ToDashboardPhoto(PhotoDto photo, string apiBaseUrl)
    {
        var thumb = ResolveThumbnailUrl(apiBaseUrl, photo.FileId, photo.ThumbnailUrl)
                    ?? GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, photo.PreviewUrl);
        return new DashboardPhotoDto
        {
            MediaId = GalleryBackendMapper.ParseFileId(photo.FileId),
            AbsoluteLibraryPath = thumb ?? string.Empty,
            FallbackAbsoluteLibraryPath = GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, photo.PreviewUrl),
            FileName = photo.Filename,
            IsFavorite = photo.Favorite,
            PlaceName = FirstNonEmpty(photo.PlaceDisplayName, photo.PlaceName),
            Country = photo.Country,
            CapturedAt = photo.CaptureDatetime,
        };
    }

    private static DashboardStatisticsDto MapStatistics(
        Application.DTOs.Gallery.StatisticsDto stats,
        int placeCount,
        DateTimeOffset lastUpdated = default,
        int favoriteCount = 0)
    {
        var byYear = stats.ByYear
            .OrderBy(x => x.Name)
            .Select(x => new DashboardStatBucketDto { Name = x.Name, Count = x.Count })
            .ToList();
        var byCountry = stats.ByCountry
            .OrderByDescending(x => x.Count)
            .Select(x => new DashboardStatBucketDto { Name = x.Name, Count = x.Count })
            .ToList();
        var countrySummary = byCountry.Count == 0
            ? "기록된 국가 없음"
            : string.Join(", ", byCountry.Take(3).Select(x => x.Name));

        return new DashboardStatisticsDto
        {
            PhotoCount = stats.TotalPhotos,
            PlaceCount = placeCount > 0
                ? placeCount
                : (stats.ByCountry.Count > 0 ? stats.ByCountry.Sum(x => x.Count) : stats.ByYear.Sum(x => x.Count)),
            GpsCount = stats.GpsCount,
            CountryCount = stats.ByCountry.Count,
            VisitRecordCount = stats.GpsCount,
            FavoriteCount = favoriteCount,
            TagCount = stats.AiTagCount,
            CountrySummary = countrySummary,
            LastUpdatedText = lastUpdated == default
                ? DateTime.Now.ToString("yyyy.MM.dd")
                : lastUpdated.ToLocalTime().ToString("yyyy.MM.dd"),
            ByYear = byYear,
            ByCountry = byCountry,
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
            .GroupBy(marker => PlaceIdentity.MapPlaceKey(marker.PlaceName))
            .Select(group =>
            {
                var first = group.First();
                var years = group.Where(m => m.Year.HasValue).Select(m => m.Year!.Value).Distinct().OrderByDescending(y => y).ToList();
                var rep = first;
                var thumb = ResolveThumbnailUrl(apiBaseUrl, rep.FileId, rep.Thumbnail);
                var mediaId = GalleryBackendMapper.ParseFileId(rep.FileId);
                var resolved = PlaceIdentity.ResolveCoordinates(
                    (rep.Latitude, rep.Longitude),
                    group.Select(m => (m.Latitude, m.Longitude)));
                return new VisitRecordPlaceDto
                {
                    PlaceId = PlaceIdentity.MapStableId(first.PlaceName),
                    PlaceName = PlaceIdentity.DisplayName(first.PlaceName),
                    Country = string.Empty,
                    City = string.Empty,
                    Latitude = resolved?.Latitude ?? 0,
                    Longitude = resolved?.Longitude ?? 0,
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

    private static VisitRecordPlaceDto BuildFastVisitPlace(
        Guid placeId,
        IReadOnlyList<MapMarkerDto> markers,
        IReadOnlyDictionary<Guid, TravelPlaceAggregateRaw> aggregatesById,
        string apiBaseUrl,
        GalleryHierarchyQuery query)
    {
        var first = markers[0];
        aggregatesById.TryGetValue(placeId, out var aggregate);
        var placeName = FirstNonEmpty(
            aggregate?.PlaceName,
            first.PlaceDisplayName,
            first.PlaceName,
            first.GeocodedPlaceName,
            first.PlaceCanonicalName);
        var country = FirstNonEmpty(aggregate?.Country, first.Country) ?? string.Empty;
        var city = FirstNonEmpty(aggregate?.Region, first.Province, first.City, first.District) ?? string.Empty;
        var dates = (aggregate?.VisitDates ?? [])
            .Select(DateOnly.FromDateTime)
            .Distinct()
            .Where(date => query.Year is null || date.Year == query.Year)
            .Where(date => query.Season is null || IsInSeason(date.Month, query.Season.Value))
            .OrderBy(date => date)
            .ToList();
        var representative = aggregate?.RepresentativeMediaId is Guid representativeId
            ? markers.FirstOrDefault(marker => GalleryBackendMapper.ParseFileId(marker.FileId) == representativeId) ?? first
            : first;
        var representativeMediaId = aggregate?.RepresentativeMediaId
                                    ?? NonEmptyMediaId(representative.FileId);
        var representativePath = aggregate?.AbsoluteLibraryPath
                                 ?? ResolveThumbnailUrl(apiBaseUrl, representative.FileId, representative.Thumbnail);
        var resolvedCoordinates = PlaceIdentity.ResolveCoordinates(
            (representative.Latitude, representative.Longitude),
            markers.Select(marker => (marker.Latitude, marker.Longitude)));
        var photos = markers.Select(marker => ToPreview(marker, apiBaseUrl)).ToList();
        var visitCount = dates.Count > 0
            ? CountDateRanges(dates)
            : query.Year is null && query.Season is null
                ? aggregate?.ResolvedVisitCount ?? 0
                : 0;
        var photoCount = query.Year.HasValue || query.Season.HasValue
            ? markers.Count
            : aggregate?.PhotoCount ?? markers.Count;
        var years = dates.Select(date => date.Year)
            .Concat(markers.Where(marker => marker.Year.HasValue).Select(marker => marker.Year!.Value))
            .Distinct()
            .OrderByDescending(year => year)
            .ToList();

        return new VisitRecordPlaceDto
        {
            PlaceId = placeId,
            PlaceName = PlaceIdentity.DisplayName(placeName),
            Country = country,
            City = city,
            Latitude = resolvedCoordinates?.Latitude ?? 0,
            Longitude = resolvedCoordinates?.Longitude ?? 0,
            PhotoCount = photoCount,
            VisitRecordCount = visitCount,
            FavoriteCount = aggregate?.FavoriteCount ?? 0,
            RepresentativeMediaId = representativeMediaId,
            RepresentativeAbsolutePath = representativePath,
            FirstCapturedDate = dates.Count > 0 ? ToDateOffset(dates[0]) : null,
            LastCapturedDate = dates.Count > 0 ? ToDateOffset(dates[^1]) : null,
            VisitDates = dates,
            CaptureYears = years,
            AllPhotos = photos,
            PreviewPhotos = photos.Take(8).ToList(),
            MarkerScale = VisitRecordPlaceScoping.CalculateMarkerScale(visitCount, photoCount),
            IsUnclassified = aggregate?.IsUnclassified == true || string.IsNullOrWhiteSpace(placeName),
        };
    }

    private static bool MatchesVisitQuery(VisitRecordPlaceDto place, GalleryHierarchyQuery query)
    {
        if (query.UnclassifiedOnly && !place.IsUnclassified)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Country)
            && !string.Equals(place.Country, query.Country.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.City)
            && !string.Equals(place.City, query.City.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.PlaceId is Guid placeId && place.PlaceId != placeId)
        {
            return false;
        }

        if (query.Season is not null && place.VisitDates.Count == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query.SearchText))
        {
            return true;
        }

        var term = query.SearchText.Trim();
        return place.PlaceName.Contains(term, StringComparison.OrdinalIgnoreCase)
               || place.Country.Contains(term, StringComparison.OrdinalIgnoreCase)
               || place.City.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static Guid MarkerPlaceId(MapMarkerDto marker)
    {
        if (marker.MemorykeeperPlaceId is Guid id && id != Guid.Empty)
        {
            return id;
        }

        var name = FirstNonEmpty(
            marker.PlaceDisplayName,
            marker.PlaceName,
            marker.GeocodedPlaceName,
            marker.PlaceCanonicalName);
        return PlaceIdentity.StableId(marker.Country, FirstNonEmpty(marker.City, marker.District, marker.Province), name);
    }

    private static Guid? NonEmptyMediaId(string? fileId)
    {
        var id = GalleryBackendMapper.ParseFileId(fileId);
        return id == Guid.Empty ? null : id;
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

    private static bool IsInSeason(int month, TravelSeason season) => season switch
    {
        TravelSeason.Spring => month is >= 3 and <= 5,
        TravelSeason.Summer => month is >= 6 and <= 8,
        TravelSeason.Autumn => month is >= 9 and <= 11,
        TravelSeason.Winter => month is 12 or 1 or 2,
        _ => true,
    };

    private static DateTimeOffset ToDateOffset(DateOnly date) =>
        new(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<VisitRecordPlaceDto> GroupPhotosToVisitPlaces(
        IReadOnlyList<PhotoDto> photos,
        string apiBaseUrl,
        IReadOnlyList<MapMarkerDto>? markers = null,
        IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto>? locationMetadataByFileId = null)
    {
        var coordsByFileId = BuildCoordsByFileId(markers, locationMetadataByFileId);

        return photos
            // Align with map markers: place_name only (not country|city|placeName).
            .GroupBy(photo => PlaceIdentity.MapPlaceKey(
                ResolvePlaceName(photo, locationMetadataByFileId)))
            .Select(group =>
            {
                var list = group.ToList();
                var first = list[0];
                var country = list.Select(p => FirstNonEmpty(
                        p.Country,
                        LookupLocationMetadata(locationMetadataByFileId, p.FileId)?.Country))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
                var city = list.Select(p => FirstNonEmpty(
                        p.City,
                        LookupLocationMetadata(locationMetadataByFileId, p.FileId)?.City,
                        LookupLocationMetadata(locationMetadataByFileId, p.FileId)?.District))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
                var placeName = list.Select(p => ResolvePlaceName(p, locationMetadataByFileId))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var dates = list.Select(p => p.CaptureDatetime).Where(d => d.HasValue).Select(d => d!.Value).OrderBy(d => d).ToList();
                var years = dates.Select(d => d.Year).Distinct().OrderByDescending(y => y).ToList();
                var rep = list.FirstOrDefault(p => p.Favorite) ?? first;
                var mediaId = GalleryBackendMapper.ParseFileId(rep.FileId);
                var path = ResolveThumbnailUrl(apiBaseUrl, rep.FileId, rep.ThumbnailUrl)
                           ?? GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, rep.PreviewUrl);

                var groupCoords = list
                    .Select(p => LookupCoords(coordsByFileId, p.FileId))
                    .Where(c => c.HasValue)
                    .Select(c => c!.Value)
                    .ToList();
                var repCoords = LookupCoords(coordsByFileId, rep.FileId);
                var resolved = PlaceIdentity.ResolveCoordinates(repCoords, groupCoords);

                return new VisitRecordPlaceDto
                {
                    PlaceId = PlaceIdentity.MapStableId(placeName),
                    PlaceName = PlaceIdentity.DisplayName(placeName),
                    Country = country,
                    City = city,
                    Latitude = resolved?.Latitude ?? 0,
                    Longitude = resolved?.Longitude ?? 0,
                    PhotoCount = list.Count,
                    VisitRecordCount = dates.Select(d => d.Date).Distinct().Count(),
                    FavoriteCount = list.Count(p => p.Favorite),
                    RepresentativeMediaId = mediaId == Guid.Empty ? null : mediaId,
                    RepresentativeAbsolutePath = path,
                    CaptureYears = years,
                    FirstCapturedDate = dates.FirstOrDefault(),
                    LastCapturedDate = dates.LastOrDefault(),
                    AllPhotos = list.Select(p => ToPreview(p, apiBaseUrl)).ToList(),
                    PreviewPhotos = list.Take(8).Select(p => ToPreview(p, apiBaseUrl)).ToList(),
                    MarkerScale = 1.0,
                    IsUnclassified = string.IsNullOrWhiteSpace(placeName),
                };
            })
            .OrderByDescending(p => p.LastCapturedDate)
            .ToList();
    }

    public static Guid StablePlaceId(string? country, string? city, string? placeName) =>
        PlaceIdentity.StableId(country, city, placeName);

    private static string PlaceKey(string? country, string? city, string? placeName) =>
        PlaceIdentity.Key(country, city, placeName);

    private static Dictionary<string, (double Lat, double Lon)> BuildCoordsByFileId(
        IReadOnlyList<MapMarkerDto>? markers,
        IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto>? locationMetadataByFileId = null)
    {
        var dict = new Dictionary<string, (double Lat, double Lon)>(StringComparer.OrdinalIgnoreCase);
        foreach (var marker in markers ?? [])
        {
            var fileId = marker.FileId?.Trim();
            if (string.IsNullOrWhiteSpace(fileId)
                || !PlaceIdentity.HasValidCoordinates(marker.Latitude, marker.Longitude))
            {
                continue;
            }

            if (!dict.ContainsKey(fileId))
            {
                dict[fileId] = (marker.Latitude, marker.Longitude);
            }
        }

        if (locationMetadataByFileId is not null)
        {
            foreach (var (fileId, metadata) in locationMetadataByFileId)
            {
                if (metadata.Latitude is double latitude
                    && metadata.Longitude is double longitude
                    && PlaceIdentity.HasValidCoordinates(latitude, longitude))
                {
                    dict.TryAdd(fileId, (latitude, longitude));
                }
            }
        }

        return dict;
    }

    private static string? ResolvePlaceName(
        PhotoDto photo,
        IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto>? locationMetadataByFileId) =>
        FirstNonEmpty(
            photo.PlaceName,
            LookupLocationMetadata(locationMetadataByFileId, photo.FileId)?.PlaceName);

    private static GalleryPhotoLocationMetadataDto? LookupLocationMetadata(
        IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto>? locationMetadataByFileId,
        string? fileId)
    {
        var key = fileId?.Trim();
        return locationMetadataByFileId is not null
               && !string.IsNullOrWhiteSpace(key)
               && locationMetadataByFileId.TryGetValue(key, out var metadata)
            ? metadata
            : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(value => value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static (double Latitude, double Longitude)? LookupCoords(
        IReadOnlyDictionary<string, (double Lat, double Lon)> coordsByFileId,
        string? fileId)
    {
        var key = fileId?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return coordsByFileId.TryGetValue(key, out var coords)
            ? (coords.Lat, coords.Lon)
            : null;
    }
    private static VisitRecordPreviewPhotoDto ToPreview(MapMarkerDto marker, string apiBaseUrl)
    {
        var fileId = marker.FileId ?? string.Empty;
        var id = GalleryBackendMapper.ParseFileId(fileId);
        var thumb = ResolveThumbnailUrl(apiBaseUrl, fileId, marker.Thumbnail);
        return new VisitRecordPreviewPhotoDto
        {
            MediaId = id,
            BackendFileId = fileId,
            FileName = string.IsNullOrWhiteSpace(marker.PlaceName) ? fileId : marker.PlaceName!,
            ThumbnailUrl = thumb ?? string.Empty,
            AbsoluteLibraryPath = thumb ?? string.Empty,
            IsFavorite = false,
            CapturedAt = null,
            CaptureYear = marker.Year ?? 0,
        };
    }

    private static VisitRecordPreviewPhotoDto ToPreview(PhotoDto photo, string apiBaseUrl)
    {
        var fileId = photo.FileId ?? string.Empty;
        var id = GalleryBackendMapper.ParseFileId(fileId);
        var thumb = ResolveThumbnailUrl(apiBaseUrl, fileId, photo.ThumbnailUrl)
                    ?? GalleryBackendMapper.ToAbsoluteUrl(apiBaseUrl, photo.PreviewUrl);
        return new VisitRecordPreviewPhotoDto
        {
            MediaId = id,
            BackendFileId = fileId,
            FileName = photo.Filename,
            ThumbnailUrl = thumb ?? string.Empty,
            AbsoluteLibraryPath = thumb ?? string.Empty,
            IsFavorite = photo.Favorite,
            CapturedAt = photo.CaptureDatetime,
            CaptureYear = photo.CaptureDatetime?.Year ?? 0,
        };
    }

    /// <summary>
    /// Absolute thumbnail URL from API field, or synthesized from file_id (no Backend change).
    /// </summary>
    internal static string? ResolveThumbnailUrl(string apiBaseUrl, string? fileId, string? thumbnailField)
        => BackendMediaUrlResolver.ResolveThumbnailUrl(apiBaseUrl, fileId, thumbnailField);
}
