using System.Globalization;
using System.Text;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

public sealed class FastGalleryApiRepository : IFastGalleryApiRepository
{
    private const string Root = "/api/memorykeeper/gallery";
    private readonly BaseApiClient _apiClient;

    public FastGalleryApiRepository(BaseApiClient apiClient) => _apiClient = apiClient;

    public async Task<FastGalleryPhotoPageDto> GetPhotosAsync(FastGalleryPhotoQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var limit = Math.Clamp(query.Limit, 1, 100);
        var path = BuildPath($"{Root}/photos", new Dictionary<string, string?>
        {
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            ["cursor"] = query.Cursor,
            ["year"] = query.Year?.ToString(CultureInfo.InvariantCulture),
            ["country"] = query.Country,
            ["region"] = query.Region,
            ["place_id"] = query.PlaceId?.ToString("D"),
            ["favorite"] = query.Favorite?.ToString().ToLowerInvariant(),
            ["has_gps"] = query.HasGps?.ToString().ToLowerInvariant(),
            ["date_from"] = query.DateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["date_to"] = query.DateTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        });
        return (await _apiClient.GetAsync<FastGalleryPhotoPageDto>(path, cancellationToken).ConfigureAwait(false)).Data
               ?? new FastGalleryPhotoPageDto();
    }

    public async Task<FastGallerySummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        (await _apiClient.GetAsync<FastGallerySummaryDto>($"{Root}/summary", cancellationToken).ConfigureAwait(false)).Data
        ?? new FastGallerySummaryDto();

    public async Task<FastGalleryHierarchyDto> GetHierarchyAsync(CancellationToken cancellationToken = default) =>
        (await _apiClient.GetAsync<FastGalleryHierarchyDto>($"{Root}/hierarchy", cancellationToken).ConfigureAwait(false)).Data
        ?? new FastGalleryHierarchyDto();

    private static string BuildPath(string root, IReadOnlyDictionary<string, string?> query)
    {
        var parts = query.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}");
        var suffix = string.Join("&", parts);
        return string.IsNullOrEmpty(suffix) ? root : $"{root}?{suffix}";
    }
}
