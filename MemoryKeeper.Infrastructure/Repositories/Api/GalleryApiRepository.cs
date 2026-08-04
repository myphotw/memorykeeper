using System.Globalization;
using System.Text;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;
using GalleryDtos = MemoryKeeper.Application.DTOs.Gallery;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

/// <summary>
/// Gallery queries via TC-Backend V1.0 Common Gallery API. No SQLite access.
/// </summary>
public sealed class GalleryApiRepository : IGalleryApiRepository
{
    private const string GalleryRoot = "/api/common/gallery";

    private readonly BaseApiClient _apiClient;

    public GalleryApiRepository(BaseApiClient apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<PagedResult<GalleryDtos.PhotoDto>> GetPhotosAsync(
        int page = 1,
        int pageSize = 20,
        string sort = "capture_datetime_desc",
        string? serviceName = null,
        CancellationToken cancellationToken = default)
    {
        var path = BuildPath(GalleryRoot, new Dictionary<string, string?>
        {
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["page_size"] = pageSize.ToString(CultureInfo.InvariantCulture),
            ["sort"] = sort,
            ["service_name"] = ResolveServiceName(serviceName),
        });

        var response = await _apiClient.GetAsync<PagedResult<GalleryDtos.PhotoDto>>(path, cancellationToken)
            .ConfigureAwait(false);
        return response.Data ?? new PagedResult<GalleryDtos.PhotoDto>();
    }

    public async Task<GalleryDtos.PhotoDetailDto> GetPhotoAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var path = $"{GalleryRoot}/{Uri.EscapeDataString(fileId.ToString("D"))}";
        var response = await _apiClient.GetAsync<GalleryDtos.PhotoDetailDto>(path, cancellationToken)
            .ConfigureAwait(false);
        return response.Data
            ?? throw new ApiException(
                System.Net.HttpStatusCode.NotFound,
                $"Gallery detail returned empty body for file_id={fileId}");
    }

    public async Task<PagedResult<GalleryDtos.PhotoDto>> SearchAsync(
        int? year = null,
        string? country = null,
        string? city = null,
        string? camera = null,
        string? tag = null,
        bool? favorite = null,
        string? serviceName = null,
        DateTimeOffset? dateFrom = null,
        DateTimeOffset? dateTo = null,
        string? keyword = null,
        int page = 1,
        int pageSize = 20,
        string sort = "capture_datetime_desc",
        string? province = null,
        string? district = null,
        string? place = null,
        CancellationToken cancellationToken = default)
    {
        // V1.0 Search API has no province/district/place fields — fold into keyword.
        var mergedKeyword = MergeKeyword(keyword, province, district, place);

        var path = BuildPath($"{GalleryRoot}/search", new Dictionary<string, string?>
        {
            ["year"] = year?.ToString(CultureInfo.InvariantCulture),
            ["country"] = country,
            ["city"] = city,
            ["camera"] = camera,
            ["tag"] = tag,
            ["favorite"] = favorite?.ToString().ToLowerInvariant(),
            ["service_name"] = ResolveServiceName(serviceName),
            ["date_from"] = dateFrom?.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            ["date_to"] = dateTo?.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            ["keyword"] = mergedKeyword,
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["page_size"] = pageSize.ToString(CultureInfo.InvariantCulture),
            ["sort"] = sort,
        });

        var response = await _apiClient.GetAsync<PagedResult<GalleryDtos.PhotoDto>>(path, cancellationToken)
            .ConfigureAwait(false);
        return response.Data ?? new PagedResult<GalleryDtos.PhotoDto>();
    }

    public async Task<GalleryDtos.MapResultDto> GetMapAsync(
        int? year = null,
        string? serviceName = null,
        CancellationToken cancellationToken = default)
    {
        var path = BuildPath($"{GalleryRoot}/map", new Dictionary<string, string?>
        {
            ["year"] = year?.ToString(CultureInfo.InvariantCulture),
            ["service_name"] = ResolveServiceName(serviceName),
        });

        var response = await _apiClient.GetAsync<GalleryDtos.MapResultDto>(path, cancellationToken)
            .ConfigureAwait(false);
        return response.Data ?? new GalleryDtos.MapResultDto();
    }

    public async Task<GalleryDtos.TimelineResultDto> GetTimelineAsync(
        string? serviceName = null,
        CancellationToken cancellationToken = default)
    {
        var path = BuildPath($"{GalleryRoot}/timeline", new Dictionary<string, string?>
        {
            ["service_name"] = ResolveServiceName(serviceName),
        });

        var response = await _apiClient.GetAsync<GalleryDtos.TimelineResultDto>(path, cancellationToken)
            .ConfigureAwait(false);
        return response.Data ?? new GalleryDtos.TimelineResultDto();
    }

    public async Task<GalleryDtos.StatisticsDto> GetStatisticsAsync(
        string? serviceName = null,
        CancellationToken cancellationToken = default)
    {
        var path = BuildPath($"{GalleryRoot}/statistics", new Dictionary<string, string?>
        {
            ["service_name"] = ResolveServiceName(serviceName),
        });

        var response = await _apiClient.GetAsync<GalleryDtos.StatisticsDto>(path, cancellationToken)
            .ConfigureAwait(false);
        return response.Data ?? new GalleryDtos.StatisticsDto();
    }

    private string ResolveServiceName(string? serviceName) =>
        string.IsNullOrWhiteSpace(serviceName) ? _apiClient.ServiceName : serviceName;

    private static string? MergeKeyword(string? keyword, string? province, string? district, string? place)
    {
        var parts = new[] { keyword, province, district, place }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToArray();
        return parts.Length == 0 ? null : string.Join(" ", parts);
    }

    private static string BuildPath(string root, IReadOnlyDictionary<string, string?> query)
    {
        var sb = new StringBuilder(root);
        var first = true;
        foreach (var (key, value) in query)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            sb.Append(first ? '?' : '&');
            first = false;
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value));
        }

        return sb.ToString();
    }
}
