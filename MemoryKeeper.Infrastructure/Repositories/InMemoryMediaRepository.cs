using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Infrastructure.Database;

namespace MemoryKeeper.Infrastructure.Repositories;

/// <summary>
/// In-memory repository used by unit tests.
/// </summary>
public sealed class InMemoryMediaRepository : IMediaRepository
{
    private readonly List<Media> _items = [];

    public Task<Media?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var media = _items.FirstOrDefault(item => item.Id == id);
        return Task.FromResult(media);
    }

    public Task<Media?> GetByContentHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        var media = _items.FirstOrDefault(item => item.ContentHash == contentHash);
        return Task.FromResult(media);
    }

    public Task<IReadOnlyList<Media>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Media> result = _items.ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Media>> GetByPlaceIdAsync(Guid placeId, CancellationToken cancellationToken = default)
    {
        return GetByPlaceAsync(placeId, cancellationToken);
    }

    public Task<IReadOnlyList<Media>> GetByPlaceAsync(Guid placeId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Media> result = _items
            .Where(item => item.PlaceId == placeId)
            .OrderByDescending(item => item.IsFavorite)
            .ThenByDescending(item => item.CapturedAt)
            .ThenByDescending(item => item.ImportedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Media>> GetByYearAsync(int year, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Media> result = _items
            .Where(item => MediaQueryFilters.MatchesYear(item, year))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Media>> GetWithGpsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Media> result = _items
            .Where(item => item.Latitude is not null && item.Longitude is not null)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Media>> GetUnassignedAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Media> result = _items
            .Where(item => item.PlaceId is null)
            .OrderBy(item => item.CapturedAt)
            .ThenBy(item => item.ImportedAt)
            .ThenBy(item => item.FileName)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Media>> GetByIdsAsync(
        IReadOnlyCollection<Guid> mediaIds,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Media> result = _items
            .Where(item => mediaIds.Contains(item.Id))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Media>> SearchAsync(
        int? year,
        Guid? placeId,
        IReadOnlyCollection<Guid>? placeIds,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Media> query = _items;

        if (year.HasValue)
        {
            query = query.Where(item => MediaQueryFilters.MatchesYear(item, year.Value));
        }

        if (placeId.HasValue)
        {
            query = query.Where(item => item.PlaceId == placeId.Value);
        }

        if (placeIds is not null)
        {
            query = query.Where(item => item.PlaceId is not null && placeIds.Contains(item.PlaceId.Value));
        }

        IReadOnlyList<Media> result = query
            .OrderByDescending(item => item.IsFavorite)
            .ThenByDescending(item => item.CapturedAt)
            .ThenByDescending(item => item.ImportedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<Media?> GetPhotoDetailAsync(Guid mediaId, CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(mediaId, cancellationToken);
    }

    public Task UpdateFavoriteAsync(Guid mediaId, bool isFavorite, CancellationToken cancellationToken = default)
    {
        var media = _items.FirstOrDefault(item => item.Id == mediaId)
            ?? throw new InvalidOperationException($"Media '{mediaId}' was not found.");
        media.IsFavorite = isFavorite;
        media.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Media>> GetRelatedPhotosAsync(
        Guid placeId,
        Guid? excludeMediaId = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Media> query = _items.Where(item => item.PlaceId == placeId);
        if (excludeMediaId.HasValue)
        {
            query = query.Where(item => item.Id != excludeMediaId.Value);
        }

        IReadOnlyList<Media> result = query
            .OrderByDescending(item => item.IsFavorite)
            .ThenByDescending(item => item.CapturedAt)
            .ThenByDescending(item => item.ImportedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Media>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Media> result = _items
            .Where(item => item.IsFavorite)
            .OrderByDescending(item => item.CapturedAt)
            .ThenByDescending(item => item.ImportedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(Media media, CancellationToken cancellationToken = default)
    {
        _items.Add(media);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Media media, CancellationToken cancellationToken = default)
    {
        var index = _items.FindIndex(item => item.Id == media.Id);
        if (index >= 0)
        {
            _items[index] = media;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Media media, CancellationToken cancellationToken = default)
    {
        _items.RemoveAll(item => item.Id == media.Id);
        return Task.CompletedTask;
    }
}
