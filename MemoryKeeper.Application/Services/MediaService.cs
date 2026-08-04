using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Time;
using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application.Services;

public sealed class GalleryYearCountDto
{
    public int Year { get; init; }

    public int Count { get; init; }
}

public sealed class GallerySidebarSummaryDto
{
    public int TotalCount { get; init; }

    public int FavoriteCount { get; init; }

    public int RecentCount { get; init; }

    public int PendingCount { get; init; }

    public IReadOnlyList<GalleryYearCountDto> Years { get; init; } = [];
}

public enum GalleryQueryMode
{
    All,
    Year,
    Favorites,
    Recent,
    Pending
}

public sealed class MediaService
{
    public const int RecentGalleryTake = 48;

    private readonly IMediaRepository _mediaRepository;
    private readonly IStorageRepository _storageRepository;
    private readonly IMediaTagRepository _mediaTagRepository;
    private readonly IFileAccessService _fileAccessService;

    public MediaService(
        IMediaRepository mediaRepository,
        IStorageRepository storageRepository,
        IMediaTagRepository mediaTagRepository,
        IFileAccessService fileAccessService)
    {
        _mediaRepository = mediaRepository;
        _storageRepository = storageRepository;
        _mediaTagRepository = mediaTagRepository;
        _fileAccessService = fileAccessService;
    }

    public async Task<IReadOnlyList<MediaDto>> GetLibraryAsync(CancellationToken cancellationToken = default)
    {
        var mediaItems = await _mediaRepository.GetAllAsync(cancellationToken);

        return mediaItems
            .Select(media => new MediaDto
            {
                Id = media.Id,
                FileName = media.FileName,
                RelativePath = media.RelativePath,
                CapturedAt = DateTimeHelper.ToUtcOffset(media.CapturedAt)
            })
            .ToList();
    }

    public async Task<GallerySidebarSummaryDto> GetGallerySidebarSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var photos = await GetPhotoEntitiesAsync(cancellationToken);

        var years = photos
            .GroupBy(media => MediaDate.ResolveYear(media.CapturedAt, media.ImportedAt))
            .Select(group => new GalleryYearCountDto
            {
                Year = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(item => item.Year)
            .ToList();

        var recent = photos
            .OrderByDescending(media => media.ImportedAt)
            .Take(RecentGalleryTake)
            .Count();

        return new GallerySidebarSummaryDto
        {
            TotalCount = photos.Count,
            FavoriteCount = photos.Count(media => media.IsFavorite),
            RecentCount = recent,
            PendingCount = photos.Count(media => media.Status == MediaStatus.Pending),
            Years = years
        };
    }

    public async Task<IReadOnlyList<GalleryMediaDto>> QueryGalleryAsync(
        GalleryQueryMode mode,
        int? year = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Domain.Entities.Media> mediaItems = mode switch
        {
            GalleryQueryMode.Favorites => await _mediaRepository.GetFavoritesAsync(cancellationToken),
            GalleryQueryMode.Pending => await _mediaRepository.GetUnassignedAsync(cancellationToken),
            GalleryQueryMode.Recent => (await GetPhotoEntitiesAsync(cancellationToken))
                .OrderByDescending(media => media.ImportedAt)
                .Take(RecentGalleryTake)
                .ToList(),
            GalleryQueryMode.Year when year is int selectedYear =>
                await _mediaRepository.SearchAsync(selectedYear, placeId: null, placeIds: null, cancellationToken),
            _ => await _mediaRepository.SearchAsync(year: null, placeId: null, placeIds: null, cancellationToken)
        };

        var storages = (await _storageRepository.GetAllAsync(cancellationToken))
            .ToDictionary(storage => storage.Id);

        return mediaItems
            .Where(media => media.MediaType == MediaType.Photo)
            .Select(media => MapGallery(media, storages))
            .Where(item => item is not null)
            .Cast<GalleryMediaDto>()
            .ToList();
    }

    public async Task<IReadOnlyList<GalleryMediaDto>> SearchGalleryAsync(
        int? year,
        Guid? placeId,
        IReadOnlyCollection<Guid>? tagIds = null,
        CancellationToken cancellationToken = default)
    {
        var mediaItems = await _mediaRepository.SearchAsync(
            year,
            placeId,
            placeIds: null,
            cancellationToken);

        if (tagIds is { Count: > 0 })
        {
            var matchedIds = await _mediaTagRepository.GetMediaIdsWithAllTagsAsync(tagIds, cancellationToken);
            var idSet = matchedIds.ToHashSet();
            mediaItems = mediaItems.Where(media => idSet.Contains(media.Id)).ToList();
        }

        var storages = (await _storageRepository.GetAllAsync(cancellationToken))
            .ToDictionary(storage => storage.Id);

        return mediaItems
            .Where(media => media.MediaType == MediaType.Photo)
            .Where(media => storages.ContainsKey(media.StorageId))
            .Select(media => MapGallery(media, storages))
            .Where(item => item is not null)
            .Cast<GalleryMediaDto>()
            .ToList();
    }

    public async Task<IReadOnlyList<GalleryMediaDto>> GetFavoritesGalleryAsync(
        CancellationToken cancellationToken = default)
    {
        return await QueryGalleryAsync(GalleryQueryMode.Favorites, cancellationToken: cancellationToken);
    }

    private async Task<List<Domain.Entities.Media>> GetPhotoEntitiesAsync(CancellationToken cancellationToken)
    {
        var mediaItems = await _mediaRepository.GetAllAsync(cancellationToken);
        return mediaItems
            .Where(media => media.MediaType == MediaType.Photo)
            .ToList();
    }

    private GalleryMediaDto? MapGallery(
        Domain.Entities.Media media,
        IReadOnlyDictionary<Guid, Domain.Entities.Storage> storages)
    {
        if (!storages.TryGetValue(media.StorageId, out var storage))
        {
            return null;
        }

        return new GalleryMediaDto
        {
            Id = media.Id,
            FileName = media.FileName,
            AbsoluteLibraryPath = ResolveLibraryPathSafe(storage.PhotoRoot, media),
            CapturedAt = DateTimeHelper.ToUtcOffset(media.CapturedAt),
            PlaceId = media.PlaceId,
            MediaType = media.MediaType,
            IsFavorite = media.IsFavorite
        };
    }

    private string ResolveLibraryPathSafe(string photoRoot, Domain.Entities.Media media)
    {
        var relativePath = string.IsNullOrWhiteSpace(media.RelativePath)
            ? media.FileName
            : media.RelativePath;

        try
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            return _fileAccessService.ResolveAbsolutePath(photoRoot, relativePath);
        }
        catch
        {
            // Missing/invalid RelativePath must not fail the whole gallery query.
            // Thumbnail layer treats empty/missing files as placeholder.
            return string.Empty;
        }
    }
}
