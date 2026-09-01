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
            PhotoCount = item.PhotoCount,
            VisitCount = item.VisitCount,
            VisitDates = item.CaptureDates.Select(date => date.ToDateTime(TimeOnly.MinValue)).OrderBy(date => date).ToList(),
            IsUnclassified = string.IsNullOrWhiteSpace(item.PlaceDisplayName),
            RepresentativeMediaId = ToMediaId(item.RepresentativeFileId),
            AbsoluteLibraryPath = ToAbsoluteUrl(FirstUrl(item.RepresentativeThumbnailUrl, item.RepresentativePreviewUrl)),
            RepresentativeCaptureDate = item.RepresentativeCaptureDate,
            Photos = [],
        }).ToList();
        _logger.LogInformation("TravelRecords projected from Fast Travel aggregates. Places={Places}", result.Count);
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
            RepresentativeThumbnailPath = ToAbsoluteUrl(FirstUrl(item.RepresentativeThumbnailUrl, item.RepresentativePreviewUrl)),
            RepresentativeCaptureDate = item.RepresentativeCaptureDate,
        }).ToList();
    }

    public async Task<IReadOnlyList<TravelMemoryCandidateRaw>> GetMemoryCandidatesAsync(DateOnly referenceDate, int limit, CancellationToken cancellationToken = default)
    {
        var response = await _travel.GetMemoriesAsync(referenceDate, limit, cancellationToken).ConfigureAwait(false);
        return response.Items.Select(item => new TravelMemoryCandidateRaw
        {
            MediaId = ToMediaId(item.FileId),
            PlaceId = item.MemorykeeperPlaceId ?? item.PlaceId,
            PlaceName = item.PlaceDisplayName ?? string.Empty,
            Country = item.Country ?? string.Empty,
            CaptureDate = item.EffectiveCaptureDate,
            ThumbnailPath = ToAbsoluteUrl(FirstUrl(item.ThumbnailUrl, item.PreviewUrl)) ?? string.Empty,
            Category = item.Category ?? item.Candidate ?? string.Empty,
        }).ToList();
    }

    private static Guid? ToMediaId(string? fileId)
    {
        var value = BackendFileIdCodec.ToGuid(fileId);
        return value == Guid.Empty ? null : value;
    }

    private static string? FirstUrl(string? thumbnail, string? preview) =>
        string.IsNullOrWhiteSpace(thumbnail) ? preview : thumbnail;

    private string? ToAbsoluteUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Uri.TryCreate(value, UriKind.Absolute, out _)) return value;
        return $"{_apiClient.ApiBaseUrl.TrimEnd('/')}/{value.TrimStart('/')}";
    }

    private Task<FastTravelAggregatesDto> GetAggregatesAsync(CancellationToken cancellationToken)
    {
        // A scoped repository serves one TravelRecords load; share its aggregate response without
        // pretending to implement cross-session sync/cache semantics.
        return _aggregatesTask ??= _travel.GetAggregatesAsync(cancellationToken);
    }
}

