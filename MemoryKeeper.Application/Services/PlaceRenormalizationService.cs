using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Recomputes CanonicalName for all places and merges duplicates (MK-042Q).
/// </summary>
public sealed class PlaceRenormalizationService
{
    private readonly IPlaceRepository _placeRepository;
    private readonly IMediaRepository _mediaRepository;
    private readonly IMediaLibraryPathSyncService _pathSyncService;
    private readonly IPlaceDisplayNameRefreshService _placeDisplayNameRefreshService;
    private readonly ILogger<PlaceRenormalizationService> _logger;

    public PlaceRenormalizationService(
        IPlaceRepository placeRepository,
        IMediaRepository mediaRepository,
        IMediaLibraryPathSyncService pathSyncService,
        IPlaceDisplayNameRefreshService placeDisplayNameRefreshService,
        ILogger<PlaceRenormalizationService> logger)
    {
        _placeRepository = placeRepository;
        _mediaRepository = mediaRepository;
        _pathSyncService = pathSyncService;
        _placeDisplayNameRefreshService = placeDisplayNameRefreshService;
        _logger = logger;
    }

    public async Task<MaintenanceResultDto> RenormalizeAndMergeAsync(CancellationToken cancellationToken = default)
    {
        ImportPipelineLog.Write("장소 재정규화 시작");

        var places = (await _placeRepository.GetAllAsync(cancellationToken)).ToList();
        foreach (var place in places)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seed = !string.IsNullOrWhiteSpace(place.CanonicalName)
                ? place.CanonicalName
                : !string.IsNullOrWhiteSpace(place.DisplayName)
                    ? place.DisplayName
                    : place.City;
            var canonical = PlaceNormalizer.BuildCanonicalName(seed);
            var country = PlaceNormalizer.NormalizeCountry(place.Country);
            var province = PlaceNormalizer.NormalizeRegion(place.Province);
            var city = PlaceNormalizer.NormalizePlace(place.City);

            place.CanonicalName = canonical;
            place.Country = country;
            place.Province = string.IsNullOrWhiteSpace(province) ? place.Province : province;
            place.City = string.IsNullOrWhiteSpace(city) ? place.City : city;

            var displayLabel = PlaceNormalizer.GetDisplayLabel(place);
            var displayHasHangul = place.DisplayName.Any(ch => ch is >= '\uAC00' and <= '\uD7A3');
            var labelHasHangul = displayLabel.Any(ch => ch is >= '\uAC00' and <= '\uD7A3');
            if (labelHasHangul
                && (string.IsNullOrWhiteSpace(place.DisplayName)
                    || !displayHasHangul
                    || PlaceNormalizer.CanonicalEquals(place.DisplayName, canonical)))
            {
                place.DisplayName = displayLabel;
            }

            place.UpdatedAt = DateTime.UtcNow;
            await _placeRepository.UpdateAsync(place, cancellationToken);
        }

        var refreshed = await _placeDisplayNameRefreshService.RefreshKoreanNamesAsync(places, cancellationToken);
        if (refreshed > 0)
        {
            ImportPipelineLog.Write($"한국어 장소명 Google 갱신 {refreshed}건");
        }

        var groups = places
            .Where(place => !string.IsNullOrWhiteSpace(place.CanonicalName))
            .GroupBy(place => place.CanonicalName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();

        var mergedGroups = 0;
        var deletedPlaces = 0;
        var reassignedMedia = 0;

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var members = group.ToList();
            var survivor = ChooseSurvivor(members);
            var duplicates = members.Where(place => place.Id != survivor.Id).ToList();

            ImportPipelineLog.Write(
                $"Canonical 병합 {group.Key} Survivor={survivor.Id} Duplicates={duplicates.Count}");

            foreach (var duplicate in duplicates)
            {
                var mediaItems = await _mediaRepository.GetByPlaceIdAsync(duplicate.Id, cancellationToken);
                foreach (var media in mediaItems)
                {
                    media.PlaceId = survivor.Id;
                    media.UpdatedAt = DateTime.UtcNow;
                    await _mediaRepository.UpdateAsync(media, cancellationToken);
                    await _pathSyncService.SyncMediaPathAsync(media, survivor, cancellationToken);
                    reassignedMedia++;
                }

                await _placeRepository.DeleteAsync(duplicate, cancellationToken);
                deletedPlaces++;
            }

            mergedGroups++;
        }

        var message =
            $"장소 재정규화 완료. 장소 {places.Count}건, 병합 그룹 {mergedGroups}, 삭제 {deletedPlaces}, 미디어 재연결 {reassignedMedia}.";
        ImportPipelineLog.Write(message);
        _logger.LogInformation("{Message}", message);

        return new MaintenanceResultDto
        {
            Succeeded = true,
            Message = message
        };
    }

    private static Place ChooseSurvivor(IReadOnlyList<Place> members)
    {
        return members
            .OrderByDescending(place => !string.IsNullOrWhiteSpace(place.GooglePlaceId))
            .ThenByDescending(place => place.IsFavorite)
            .ThenByDescending(place => place.UsageCount)
            .ThenBy(place => place.CreatedAt)
            .First();
    }
}
