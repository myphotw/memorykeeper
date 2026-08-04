using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Application.Interfaces;

public interface IMediaRepository
{
    Task<Media?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Media?> GetByContentHashAsync(string contentHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Media>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Media>> GetByPlaceIdAsync(Guid placeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Media>> GetByPlaceAsync(Guid placeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Media>> GetByYearAsync(int year, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Media>> GetWithGpsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Media with Status=Pending or PlaceId=null (unfinished memories).
    /// </summary>
    Task<IReadOnlyList<Media>> GetUnassignedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Media>> GetByIdsAsync(
        IReadOnlyCollection<Guid> mediaIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Media>> SearchAsync(
        int? year,
        Guid? placeId,
        IReadOnlyCollection<Guid>? placeIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads media with Place and Storage for Photo Detail.
    /// </summary>
    Task<Media?> GetPhotoDetailAsync(Guid mediaId, CancellationToken cancellationToken = default);

    Task UpdateFavoriteAsync(Guid mediaId, bool isFavorite, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same-place photos ordered by favorite priority then capture time.
    /// </summary>
    Task<IReadOnlyList<Media>> GetRelatedPhotosAsync(
        Guid placeId,
        Guid? excludeMediaId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Favorite media for future favorite-only / slideshow / best-memory features.
    /// </summary>
    Task<IReadOnlyList<Media>> GetFavoritesAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Media media, CancellationToken cancellationToken = default);

    Task UpdateAsync(Media media, CancellationToken cancellationToken = default);

    Task DeleteAsync(Media media, CancellationToken cancellationToken = default);
}
