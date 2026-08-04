using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

/// <summary>
/// Polls TC-Backend <c>GET /api/common/upload/jobs/{job_id}</c>. No SQLite access.
/// </summary>
public sealed class UploadJobApiRepository : IUploadJobApiRepository
{
    private const string JobsRoot = "/api/common/upload/jobs";

    private readonly BaseApiClient _apiClient;

    public UploadJobApiRepository(BaseApiClient apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<UploadJobStatusDto> GetStatusAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var path = $"{JobsRoot}/{Uri.EscapeDataString(jobId.ToString("D"))}";
        var response = await _apiClient
            .GetAsync<UploadJobStatusDto>(path, cancellationToken)
            .ConfigureAwait(false);

        return response.Data
            ?? throw new ApiException(
                System.Net.HttpStatusCode.NotFound,
                $"Upload job status returned empty body for job_id={jobId}");
    }
}
