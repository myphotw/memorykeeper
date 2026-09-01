using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

/// <summary>Projects the Fast Travel aggregate/memory contracts into established TravelRecords raw models.</summary>
public sealed class FastGalleryTravelRecordsRepository : ITravelRecordsRepository
{
    private readonly IFastTravelApiRepository _travel;
    private readonly BaseApiClient _apiClient;
    private readonly ILogger<FastGalleryTravelRecordsRepository> _logger;
    private Task<FastTravelAggregatesDto>? _aggregatesTask;

    public FastGalleryTravelRecordsRepository(IFastTravelApiRepository travel, BaseApiClient apiClient, ILogger<FastGalleryTravelRecordsRepository> logger)
    {
        _travel = travel;
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TravelPlaceAggregateRaw>> GetPlaceAggregatesAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetAggregatesAsync(cancellationToken).ConfigureAwait(false);
        var result = response.Places.Where(item => item.MemorykeeperPlaceId.HasValue).Select(item => new TravelPlaceAggregateRaw
        {
            PlaceId = item.MemorykeeperPlaceId!.Value,
            PlaceName = item.PlaceDisplayName ?? string.Empty,
            Country = item.Country ?? string.Empty,
            Region = item.Region ?? string.Empty,
            Latitude = item.Latitude ?? 0d,
            Longitude = item.Longitude ?? 0d,
            PhotoCount = item.PhotoCount,
            VisitCount = item.VisitCount,
            VisitDates = item.CaptureDates.Select(date => date.ToDateTime(TimeOnly.MinValue)).OrderBy(date => date).ToList(),
            IsUnclassified = string.IsNullOrWhiteSpace(item.PlaceDisplayName),
            RepresentativeMediaId = ToMediaId(item.RepresentativeFileId),
            AbsoluteLibraryPath = ResolveThumbnailUrl(
                item.RepresentativeFileId,
                item.RepresentativeThumbnailUrl,
                item.RepresentativePreviewUrl),
            RepresentativeCaptureDate = item.RepresentativeCaptureDate,
            Photos = [],
        }).ToList();
        _logger.LogInformation(
            "TravelRecords projected from Fast Travel aggregates. Places={Places}, WithCoordinates={WithCoordinates}",
            result.Count,
            result.Count(place => PlaceIdentity.HasValidCoordinates(place.Latitude, place.Longitude)));
        return result;
    }

    public async Task<IReadOnlyList<TravelCountryAggregateRaw>> GetCountryAggregatesAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetAggregatesAsync(cancellationToken).ConfigureAwait(false);
        return response.Countries.Select(item => new TravelCountryAggregateRaw
        {
            Country = item.Country ?? string.Empty,
            PhotoCount = item.PhotoCount,
            VisitCount = item.VisitCount,
            CaptureDates = item.CaptureDates,
            RepresentativeMediaId = ToMediaId(item.RepresentativeFileId),
            RepresentativeThumbnailPath = ResolveThumbnailUrl(
                item.RepresentativeFileId,
                item.RepresentativeThumbnailUrl,
                item.RepresentativePreviewUrl),
            RepresentativeCaptureDate = item.RepresentativeCaptureDate,
        }).ToList();
    }

    public async Task<IReadOnlyList<TravelMemoryCandidateRaw>> GetMemoryCandidatesAsync(DateOnly referenceDate, int limit, CancellationToken cancellationToken = default)
    {
        var response = await _travel.GetMemoriesAsync(referenceDate, limit, cancellationToken).ConfigureAwait(false);
        var candidates = response.Items.Count > 0
            ? response.Items.Select(item => (Item: item, Category: item.Category ?? item.Candidate)).ToList()
            : response.ExactAnniversary.Select(item => (Item: item, Category: (string?)"exact_anniversary"))
                .Concat(response.PreviousYearPeriod.Select(item => (Item: item, Category: (string?)"previous_year_period")))
                .ToList();

        _logger.LogInformation(
            "Fast Travel memories received. Items={Items}, ExactAnniversary={Exact}, PreviousYearPeriod={Previous}, Projected={Projected}",
            response.Items.Count,
            response.ExactAnniversary.Count,
            response.PreviousYearPeriod.Count,
            candidates.Count);

        return candidates.Select(source => new TravelMemoryCandidateRaw
        {
            MediaId = ToMediaId(source.Item.FileId),
            PlaceId = source.Item.MemorykeeperPlaceId ?? source.Item.PlaceId,
            PlaceName = source.Item.PlaceDisplayName ?? string.Empty,
            Country = source.Item.Country ?? string.Empty,
            CaptureDate = source.Item.EffectiveCaptureDate,
            ThumbnailPath = ResolveThumbnailUrl(
                                source.Item.FileId,
                                source.Item.ThumbnailUrl,
                                source.Item.PreviewUrl)
                            ?? string.Empty,
            Category = source.Category ?? string.Empty,
        }).ToList();
    }

    private static Guid? ToMediaId(string? fileId)
    {
        var value = BackendFileIdCodec.ToGuid(fileId);
        return value == Guid.Empty ? null : value;
    }

    private string? ResolveThumbnailUrl(string? fileId, string? thumbnail, string? preview) =>
        BackendMediaUrlResolver.ResolveDisplayUrl(
            _apiClient.ApiBaseUrl,
            fileId,
            thumbnail,
            preview);

    private Task<FastTravelAggregatesDto> GetAggregatesAsync(CancellationToken cancellationToken)
    {
        // A scoped repository serves one TravelRecords load; share its aggregate response without
        // pretending to implement cross-session sync/cache semantics.
        return _aggregatesTask ??= _travel.GetAggregatesAsync(cancellationToken);
    }
}

