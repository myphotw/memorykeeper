using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Time;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Composes existing search/media/tag/place services for the Visit Record screen.
/// Does not change repository contracts.
/// </summary>
public sealed class VisitRecordQueryService
{
    public static readonly Guid UnclassifiedPlaceId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const int PreviewTake = 8;
    private const int TopTagTake = 2;

    private readonly MemorySearchService _memorySearchService;
    private readonly PlaceService _placeService;
    private readonly IMediaRepository _mediaRepository;
    private readonly IMediaTagRepository _mediaTagRepository;
    private readonly ITagRepository _tagRepository;
    private readonly IStorageRepository _storageRepository;
    private readonly IFileAccessService _fileAccessService;
    private readonly ILogger<VisitRecordQueryService> _logger;

    public VisitRecordQueryService(
        MemorySearchService memorySearchService,
        PlaceService placeService,
        IMediaRepository mediaRepository,
        IMediaTagRepository mediaTagRepository,
        ITagRepository tagRepository,
        IStorageRepository storageRepository,
        IFileAccessService fileAccessService,
        ILogger<VisitRecordQueryService> logger)
    {
        _memorySearchService = memorySearchService;
        _placeService = placeService;
        _mediaRepository = mediaRepository;
        _mediaTagRepository = mediaTagRepository;
        _tagRepository = tagRepository;
        _storageRepository = storageRepository;
        _fileAccessService = fileAccessService;
        _logger = logger;
    }

    public async Task<VisitRecordQueryResult> QueryAsync(
        MemorySearchRequest? timelineRequest = null,
        CancellationToken cancellationToken = default)
    {
        return await QueryCoreAsync(timelineRequest, season: null, country: null, cancellationToken);
    }

    public Task<VisitRecordQueryResult> QueryForSeasonAsync(
        TravelSeason season,
        CancellationToken cancellationToken = default) =>
        QueryCoreAsync(null, season, country: null, cancellationToken);

    public Task<VisitRecordQueryResult> QueryForCountryAsync(
        string country,
        CancellationToken cancellationToken = default) =>
        QueryCoreAsync(null, season: null, country, cancellationToken);

    private async Task<VisitRecordQueryResult> QueryCoreAsync(
        MemorySearchRequest? timelineRequest,
        TravelSeason? season,
        string? country,
        CancellationToken cancellationToken)
    {
        var allSearch = await _memorySearchService.SearchAsync(new MemorySearchRequest(), cancellationToken);
        var timelineSearch = timelineRequest is null
            ? allSearch
            : await _memorySearchService.SearchAsync(timelineRequest, cancellationToken);

        var places = (await _placeService.GetPlaceListAsync(cancellationToken))
            .ToDictionary(place => place.Id);

        var storages = (await _storageRepository.GetAllAsync(cancellationToken))
            .ToDictionary(storage => storage.Id);

        var allMapPlaces = new List<VisitRecordPlaceDto>();
        foreach (var item in allSearch.Items)
        {
            if (!places.TryGetValue(item.PlaceId, out var place))
            {
                continue;
            }

            allMapPlaces.Add(await EnrichAsync(item, place.Latitude, place.Longitude, storages, cancellationToken));
        }

        var timelinePlaces = new List<VisitRecordPlaceDto>();
        var chips = timelineSearch.Chips.ToList();

        if (season is TravelSeason selectedSeason)
        {
            var months = TravelRecordsService.GetSeasonMonths(selectedSeason).ToHashSet();
            foreach (var item in allSearch.Items)
            {
                if (!places.TryGetValue(item.PlaceId, out var place))
                {
                    continue;
                }

                var filtered = await EnrichForSeasonAsync(
                    item,
                    place,
                    storages,
                    months,
                    cancellationToken);
                if (filtered is not null)
                {
                    timelinePlaces.Add(filtered);
                }
            }

            chips =
            [
                new MemorySearchChipDto
                {
                    Label = TravelRecordsService.GetSeasonLabel(selectedSeason),
                    Kind = MemorySearchChipKind.Year
                }
            ];
        }
        else if (!string.IsNullOrWhiteSpace(country))
        {
            var normalized = country.Trim();
            var allById = allMapPlaces.ToDictionary(place => place.PlaceId);
            foreach (var item in allSearch.Items.Where(item =>
                         item.Country.Contains(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                if (allById.TryGetValue(item.PlaceId, out var existing))
                {
                    timelinePlaces.Add(existing);
                }
            }

            chips =
            [
                new MemorySearchChipDto
                {
                    Label = normalized,
                    Kind = MemorySearchChipKind.Place
                }
            ];
        }
        else
        {
            var allById = allMapPlaces.ToDictionary(place => place.PlaceId);
            foreach (var item in timelineSearch.Items)
            {
                if (allById.TryGetValue(item.PlaceId, out var existing))
                {
                    timelinePlaces.Add(existing);
                    continue;
                }

                if (!places.TryGetValue(item.PlaceId, out var place))
                {
                    continue;
                }

                timelinePlaces.Add(await EnrichAsync(item, place.Latitude, place.Longitude, storages, cancellationToken));
            }
        }

        // Gallery years include unclassified (no PlaceId). Mirror those years on the visit timeline.
        if (season is null && string.IsNullOrWhiteSpace(country))
        {
            var unclassified = await BuildUnclassifiedPlaceAsync(storages, cancellationToken);
            if (unclassified is not null)
            {
                timelinePlaces.Add(unclassified);
            }
        }

        return new VisitRecordQueryResult
        {
            TimelinePlaces = timelinePlaces,
            AllMapPlaces = allMapPlaces,
            Chips = chips
        };
    }

    private async Task<VisitRecordPlaceDto?> BuildUnclassifiedPlaceAsync(
        IReadOnlyDictionary<Guid, Domain.Entities.Storage> storages,
        CancellationToken cancellationToken)
    {
        var unassigned = await _mediaRepository.GetUnassignedAsync(cancellationToken);
        var photos = unassigned
            .Where(media => media.MediaType == MediaType.Photo)
            .OrderByDescending(media => media.IsFavorite)
            .ThenByDescending(media => media.CapturedAt)
            .ThenByDescending(media => media.ImportedAt)
            .ToList();

        if (photos.Count == 0)
        {
            return null;
        }

        var searchStub = new MemorySearchResult
        {
            PlaceId = UnclassifiedPlaceId,
            PlaceName = GalleryHierarchyService.UnclassifiedTitle,
            Country = string.Empty,
            City = string.Empty,
            PhotoCount = photos.Count,
            VisitRecordCount = photos
                .Select(media => (media.CapturedAt ?? media.ImportedAt).Date)
                .Distinct()
                .Count(),
            FavoriteCount = photos.Count(media => media.IsFavorite),
            RepresentativeMediaId = photos[0].Id,
            FirstCapturedDate = DateTimeHelper.ToUtcOffset(photos.Min(media => media.CapturedAt ?? media.ImportedAt)),
            LastCapturedDate = DateTimeHelper.ToUtcOffset(photos.Max(media => media.CapturedAt ?? media.ImportedAt))
        };

        var dto = BuildPlaceDto(
            searchStub,
            latitude: 0,
            longitude: 0,
            photos,
            storages,
            topTags: []);
        return dto with { IsUnclassified = true, PlaceName = GalleryHierarchyService.UnclassifiedTitle };
    }

    private async Task<VisitRecordPlaceDto?> EnrichForSeasonAsync(
        MemorySearchResult item,
        PlaceDto place,
        IReadOnlyDictionary<Guid, Domain.Entities.Storage> storages,
        HashSet<int> months,
        CancellationToken cancellationToken)
    {
        var mediaItems = await _mediaRepository.GetByPlaceAsync(item.PlaceId, cancellationToken);
        var seasonPhotos = mediaItems
            .Where(media => media.MediaType == MediaType.Photo)
            .Where(media =>
            {
                var date = media.CapturedAt ?? media.ImportedAt;
                return months.Contains(DateTimeHelper.ToLocal(date).Month);
            })
            .OrderByDescending(media => media.IsFavorite)
            .ThenByDescending(media => media.CapturedAt)
            .ThenByDescending(media => media.ImportedAt)
            .ToList();

        if (seasonPhotos.Count == 0)
        {
            return null;
        }

        return BuildPlaceDto(
            item,
            place.Latitude,
            place.Longitude,
            seasonPhotos,
            storages,
            await GetTopTagsAsync(seasonPhotos.Select(media => media.Id).ToList(), cancellationToken));
    }

    private async Task<VisitRecordPlaceDto> EnrichAsync(
        MemorySearchResult item,
        double latitude,
        double longitude,
        IReadOnlyDictionary<Guid, Domain.Entities.Storage> storages,
        CancellationToken cancellationToken)
    {
        var mediaItems = await _mediaRepository.GetByPlaceAsync(item.PlaceId, cancellationToken);
        var photos = mediaItems
            .Where(media => media.MediaType == MediaType.Photo)
            .OrderByDescending(media => media.IsFavorite)
            .ThenByDescending(media => media.CapturedAt)
            .ThenByDescending(media => media.ImportedAt)
            .ToList();

        return BuildPlaceDto(
            item,
            latitude,
            longitude,
            photos,
            storages,
            await GetTopTagsAsync(photos.Select(media => media.Id).ToList(), cancellationToken));
    }

    private VisitRecordPlaceDto BuildPlaceDto(
        MemorySearchResult item,
        double latitude,
        double longitude,
        IReadOnlyList<Domain.Entities.Media> photos,
        IReadOnlyDictionary<Guid, Domain.Entities.Storage> storages,
        IReadOnlyList<string> topTags)
    {
        var allPhotos = photos
            .Where(media => storages.ContainsKey(media.StorageId))
            .Select(media => MapPreview(media, storages[media.StorageId]))
            .ToList();

        var preview = allPhotos.Take(PreviewTake).ToList();

        string? representativePath = null;
        Guid? representativeId = item.RepresentativeMediaId;
        if (representativeId is Guid repId)
        {
            var rep = allPhotos.FirstOrDefault(photo => photo.MediaId == repId)
                      ?? allPhotos.FirstOrDefault();
            if (rep is not null)
            {
                representativeId = rep.MediaId;
                representativePath = rep.AbsoluteLibraryPath;
            }
        }
        else if (preview.Count > 0)
        {
            representativeId = preview[0].MediaId;
            representativePath = preview[0].AbsoluteLibraryPath;
        }

        var visitDates = photos
            .Select(media => (media.CapturedAt ?? media.ImportedAt).Date)
            .Distinct()
            .ToList();
        var photoCount = photos.Count;
        var visitCount = visitDates.Count;

        return new VisitRecordPlaceDto
        {
            PlaceId = item.PlaceId,
            PlaceName = item.PlaceName,
            Country = item.Country,
            City = item.City,
            Latitude = latitude,
            Longitude = longitude,
            PhotoCount = photoCount,
            VisitRecordCount = visitCount,
            FavoriteCount = photos.Count(media => media.IsFavorite),
            RepresentativeMediaId = representativeId,
            RepresentativeAbsolutePath = representativePath,
            FirstCapturedDate = photos.Count == 0
                ? item.FirstCapturedDate
                : DateTimeHelper.ToUtcOffset(photos.Min(media => media.CapturedAt ?? media.ImportedAt)),
            LastCapturedDate = photos.Count == 0
                ? item.LastCapturedDate
                : DateTimeHelper.ToUtcOffset(photos.Max(media => media.CapturedAt ?? media.ImportedAt)),
            CaptureYears = photos
                .Select(media => MediaDate.ResolveYear(media.CapturedAt, media.ImportedAt))
                .Distinct()
                .OrderByDescending(year => year)
                .ToList(),
            TopTags = topTags,
            AllPhotos = allPhotos,
            PreviewPhotos = preview,
            MarkerScale = CalculateMarkerScale(visitCount, photoCount)
        };
    }

    private VisitRecordPreviewPhotoDto MapPreview(
        Domain.Entities.Media media,
        Domain.Entities.Storage storage) =>
        new()
        {
            MediaId = media.Id,
            FileName = media.FileName,
            AbsoluteLibraryPath = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, media.RelativePath),
            IsFavorite = media.IsFavorite,
            CapturedAt = DateTimeHelper.ToUtcOffset(media.CapturedAt ?? media.ImportedAt),
            CaptureYear = MediaDate.ResolveYear(media.CapturedAt, media.ImportedAt)
        };

    /// <summary>
    /// Returns a place DTO limited to photos in the given local capture year.
    /// </summary>
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

    private async Task<IReadOnlyList<string>> GetTopTagsAsync(
        IReadOnlyList<Guid> mediaIds,
        CancellationToken cancellationToken)
    {
        if (mediaIds.Count == 0)
        {
            return [];
        }

        try
        {
            var links = await _mediaTagRepository.GetByMediaIdsAsync(mediaIds, cancellationToken);
            if (links.Count == 0)
            {
                return [];
            }

            var counts = links
                .GroupBy(link => link.TagId)
                .Select(group => new { TagId = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .Take(TopTagTake * 3)
                .ToList();

            var names = new List<string>();
            foreach (var item in counts)
            {
                var tag = await _tagRepository.GetByIdAsync(item.TagId, cancellationToken);
                if (tag is null || tag.Source != TagSource.User)
                {
                    continue;
                }

                names.Add(tag.Name);
                if (names.Count >= TopTagTake)
                {
                    break;
                }
            }

            return names;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve top tags for visit record.");
            return [];
        }
    }

    public static double CalculateMarkerScale(int visitCount, int photoCount)
    {
        var score = Math.Max(1, visitCount * 2 + Math.Max(0, photoCount));
        var scale = 0.6 + Math.Log10(score + 1) * 0.45;
        return Math.Clamp(scale, 0.6, 1.7);
    }
}
