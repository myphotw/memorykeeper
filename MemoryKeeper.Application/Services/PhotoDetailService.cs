using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Time;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class PhotoDetailService
{
    private readonly IMediaRepository _mediaRepository;
    private readonly IPlaceRepository _placeRepository;
    private readonly IStorageRepository _storageRepository;
    private readonly IFileAccessService _fileAccessService;
    private readonly IMediaLibraryPathSyncService _pathSyncService;
    private readonly TagService _tagService;
    private readonly ICatalogInvalidation _catalogInvalidation;
    private readonly ILogger<PhotoDetailService> _logger;

    public PhotoDetailService(
        IMediaRepository mediaRepository,
        IPlaceRepository placeRepository,
        IStorageRepository storageRepository,
        IFileAccessService fileAccessService,
        IMediaLibraryPathSyncService pathSyncService,
        TagService tagService,
        ICatalogInvalidation catalogInvalidation,
        ILogger<PhotoDetailService> logger)
    {
        _mediaRepository = mediaRepository;
        _placeRepository = placeRepository;
        _storageRepository = storageRepository;
        _fileAccessService = fileAccessService;
        _pathSyncService = pathSyncService;
        _tagService = tagService;
        _catalogInvalidation = catalogInvalidation;
        _logger = logger;
    }

    public async Task<PhotoDetailDto> GetPhotoDetailAsync(
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        var media = await _mediaRepository.GetPhotoDetailAsync(mediaId, cancellationToken)
            ?? throw new InvalidOperationException($"Media '{mediaId}' was not found.");

        var related = media.PlaceId is Guid placeId
            ? await GetRelatedPhotosAsync(placeId, mediaId, cancellationToken)
            : [];
        var tags = await _tagService.GetMediaTagsAsync(mediaId, cancellationToken);

        return await MapDetailAsync(media, related, tags, cancellationToken);
    }

    public async Task<IReadOnlyList<RelatedPhotoDto>> GetRelatedPhotosAsync(
        Guid placeId,
        Guid? excludeMediaId = null,
        CancellationToken cancellationToken = default)
    {
        var relatedMedia = await _mediaRepository.GetRelatedPhotosAsync(
            placeId,
            excludeMediaId,
            cancellationToken);

        var storages = (await _storageRepository.GetAllAsync(cancellationToken))
            .ToDictionary(storage => storage.Id);

        return relatedMedia
            .Where(media => media.MediaType == MediaType.Photo)
            .Where(media => storages.ContainsKey(media.StorageId))
            .Select(media => MapRelated(media, storages[media.StorageId]))
            .ToList();
    }

    public async Task<bool> ToggleFavoriteAsync(
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        var media = await _mediaRepository.GetByIdAsync(mediaId, cancellationToken)
            ?? throw new InvalidOperationException($"Media '{mediaId}' was not found.");

        var next = !media.IsFavorite;
        await _mediaRepository.UpdateFavoriteAsync(mediaId, next, cancellationToken);
        _logger.LogInformation(
            "Favorite toggled. MediaId={MediaId}, IsFavorite={IsFavorite}",
            mediaId,
            next);
        return next;
    }

    public async Task<PhotoDetailDto> UpdatePlaceAsync(
        Guid mediaId,
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        var media = await _mediaRepository.GetByIdAsync(mediaId, cancellationToken)
            ?? throw new InvalidOperationException($"Media '{mediaId}' was not found.");

        var place = await _placeRepository.GetByIdAsync(placeId, cancellationToken)
            ?? throw new InvalidOperationException($"Place '{placeId}' was not found.");

        media.PlaceId = placeId;
        media.Status = MediaStatus.Imported;
        // Place registration/change should move the photo marker to the place coordinates.
        media.Latitude = place.Latitude;
        media.Longitude = place.Longitude;

        media.UpdatedAt = DateTime.UtcNow;
        await _mediaRepository.UpdateAsync(media, cancellationToken);
        await _pathSyncService.SyncMediaPathAsync(media, place, cancellationToken);
        _catalogInvalidation.Invalidate();

        _logger.LogInformation(
            "Photo place updated. MediaId={MediaId}, PlaceId={PlaceId}",
            mediaId,
            placeId);

        return await GetPhotoDetailAsync(mediaId, cancellationToken);
    }

    public async Task DeleteFromLibraryAsync(
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        var media = await _mediaRepository.GetByIdAsync(mediaId, cancellationToken)
            ?? throw new InvalidOperationException($"Media '{mediaId}' was not found.");

        var tags = await _tagService.GetMediaTagsAsync(mediaId, cancellationToken);
        if (tags.Count > 0)
        {
            await _tagService.RemoveTagsAsync(
                new RemoveTagRequest
                {
                    MediaIds = [mediaId],
                    TagIds = tags.Select(tag => tag.Id).ToList()
                },
                cancellationToken);
        }

        await _mediaRepository.DeleteAsync(media, cancellationToken);
        _logger.LogInformation(
            "Media removed from MemoryKeeper library. MediaId={MediaId}, OriginalKept={OriginalPath}, LibraryKept={RelativePath}",
            mediaId,
            media.OriginalPath,
            media.RelativePath);
    }

    /// <summary>
    /// Future-facing helper for favorite-only browsing / slideshow / best memories.
    /// </summary>
    public Task<IReadOnlyList<Media>> GetFavoriteMediaAsync(
        CancellationToken cancellationToken = default)
    {
        return _mediaRepository.GetFavoritesAsync(cancellationToken);
    }

    private async Task<PhotoDetailDto> MapDetailAsync(
        Media media,
        IReadOnlyList<RelatedPhotoDto> related,
        IReadOnlyList<TagDto> tags,
        CancellationToken cancellationToken)
    {
        var storage = media.Storage
            ?? await _storageRepository.GetByIdAsync(media.StorageId, cancellationToken)
            ?? throw new InvalidOperationException($"Storage '{media.StorageId}' was not found.");

        var place = media.Place;
        if (place is null && media.PlaceId is Guid placeId)
        {
            place = await _placeRepository.GetByIdAsync(placeId, cancellationToken);
        }

        long? fileSizeBytes = null;
        try
        {
            var absolute = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, media.RelativePath);
            if (_fileAccessService.FileExists(absolute))
            {
                fileSizeBytes = new FileInfo(absolute).Length;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "File size unavailable. MediaId={MediaId}", media.Id);
        }

        return new PhotoDetailDto
        {
            MediaId = media.Id,
            ThumbnailPath = null,
            OriginalPath = media.OriginalPath,
            RelativePath = media.RelativePath,
            AbsoluteLibraryPath = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, media.RelativePath),
            FileName = media.FileName,
            CapturedAt = DateTimeHelper.ToUtcOffset(media.CapturedAt),
            Country = place?.Country ?? string.Empty,
            Province = place?.Province ?? string.Empty,
            City = place?.City ?? string.Empty,
            Address = place?.Address ?? string.Empty,
            Latitude = media.Latitude ?? place?.Latitude,
            Longitude = media.Longitude ?? place?.Longitude,
            PlaceId = media.PlaceId,
            PlaceName = place is null ? string.Empty : PlaceNormalizer.GetDisplayLabel(place),
            CanonicalName = place?.CanonicalName,
            GooglePlaceId = place?.GooglePlaceId,
            HasGps = media.Latitude is not null && media.Longitude is not null,
            IsFavorite = media.IsFavorite,
            Width = media.Width,
            Height = media.Height,
            CameraMaker = media.CameraMaker,
            CameraModel = media.CameraModel,
            Lens = media.Lens,
            Iso = media.Iso,
            Exposure = media.Exposure,
            FNumber = media.FNumber,
            FocalLength = media.FocalLength,
            FileSizeBytes = fileSizeBytes,
            Memo = media.Memo ?? string.Empty,
            Tags = tags,
            RelatedPhotos = related
        };
    }

    public async Task<PhotoDetailDto> UpdateMemoAsync(
        Guid mediaId,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        var media = await _mediaRepository.GetByIdAsync(mediaId, cancellationToken)
            ?? throw new InvalidOperationException($"Media '{mediaId}' was not found.");

        media.Memo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
        media.UpdatedAt = DateTime.UtcNow;
        await _mediaRepository.UpdateAsync(media, cancellationToken);
        _logger.LogInformation("Photo memo updated. MediaId={MediaId}", mediaId);
        return await GetPhotoDetailAsync(mediaId, cancellationToken);
    }

    private RelatedPhotoDto MapRelated(Media media, Domain.Entities.Storage storage)
    {
        return new RelatedPhotoDto
        {
            MediaId = media.Id,
            FileName = media.FileName,
            AbsoluteLibraryPath = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, media.RelativePath),
            CapturedAt = DateTimeHelper.ToUtcOffset(media.CapturedAt),
            IsFavorite = media.IsFavorite
        };
    }
}
