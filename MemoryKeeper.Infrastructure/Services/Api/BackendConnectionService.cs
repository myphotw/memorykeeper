using System.Net;
using MemoryKeeper.Infrastructure.Services.Api.Models;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Infrastructure.Services.Api;

public sealed class BackendConnectionService
{
    private readonly BaseApiClient _apiClient;
    private readonly ILogger<BackendConnectionService> _logger;

    public BackendConnectionService(
        BaseApiClient apiClient,
        ILogger<BackendConnectionService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BackendHealthDto> GetHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _apiClient
            .GetAsync<BackendHealthDto>("/health", cancellationToken)
            .ConfigureAwait(false);
        var health = response.Data;
        if (health is null || string.IsNullOrWhiteSpace(health.Status))
        {
            throw Malformed("/health");
        }

        return health;
    }

    public async Task<BackendReadinessDto> GetReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _apiClient
            .GetAsync<BackendReadinessDto>("/api/common/readiness", cancellationToken)
            .ConfigureAwait(false);
        return response.Data ?? throw Malformed("/api/common/readiness");
    }

    public async Task<BackendCapabilitiesDto> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _apiClient
            .GetAsync<BackendCapabilitiesDto>("/api/common/capabilities", cancellationToken)
            .ConfigureAwait(false);
        return response.Data ?? throw Malformed("/api/common/capabilities");
    }

    public async Task<BackendConnectionSnapshot> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        BackendHealthDto? health = null;
        BackendReadinessDto? readiness = null;
        BackendCapabilitiesDto? capabilities = null;

        try
        {
            health = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
            readiness = await GetReadinessAsync(cancellationToken).ConfigureAwait(false);
            capabilities = await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            return new BackendConnectionSnapshot
            {
                Health = health,
                Readiness = readiness,
                Capabilities = capabilities,
            };
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                "TC-Backend connection check failed. Category={Category}, StatusCode={StatusCode}",
                ex.Category,
                (int)ex.StatusCode);
            return new BackendConnectionSnapshot
            {
                Health = health,
                Readiness = readiness,
                Capabilities = capabilities,
                ErrorCategory = ex.Category,
                ErrorMessage = ex.Message,
            };
        }
    }

    private static ApiException Malformed(string path) => new(
        HttpStatusCode.OK,
        $"TC-Backend returned a malformed response ({path}).",
        category: ApiErrorCategory.MalformedResponse);
}
