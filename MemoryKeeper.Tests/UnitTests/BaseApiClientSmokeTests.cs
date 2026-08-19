using System.Text.Json;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Tests.UnitTests;

/// <summary>
/// Explicitly opted-in, read-only checks against a deployed TC-Backend.
/// </summary>
public sealed class BaseApiClientSmokeTests
{
    private static readonly string DefaultBaseUrl =
        Environment.GetEnvironmentVariable(TcBackendOptions.ApiBaseUrlEnvironmentVariable)
        ?? TcBackendOptions.ProductionApiBaseUrl;

    [LiveBackendFact]
    public async Task Get_Root_Health_Dashboard_Succeed()
    {
        using var handle = ApiClientFactory.Create(new TcBackendOptions
        {
            ApiBaseUrl = DefaultBaseUrl,
            AuthToken = Environment.GetEnvironmentVariable(TcBackendOptions.AuthTokenEnvironmentVariable) ?? string.Empty,
            Timeout = 15,
            RetryCount = 1,
            Version = "1.0.0",
            ServiceName = "MemoryKeeper",
        });

        var client = handle.Client;

        var publicHealth = await client.GetAsync<JsonElement>("/health");
        Assert.True(publicHealth.Success);
        Assert.Equal(JsonValueKind.Object, publicHealth.Data.ValueKind);

        var readiness = await client.GetAsync<JsonElement>("/api/common/readiness");
        Assert.True(readiness.Success);
        Assert.Equal(JsonValueKind.Object, readiness.Data.ValueKind);

        var capabilities = await client.GetAsync<JsonElement>("/api/common/capabilities");
        Assert.True(capabilities.Success);
        Assert.Equal(JsonValueKind.Object, capabilities.Data.ValueKind);
    }

    [LiveBackendFact]
    public async Task ApiBaseUrl_Change_Is_Honored()
    {
        var wrongUrl = "http://127.0.0.1:9";
        using var bad = ApiClientFactory.Create(new TcBackendOptions
        {
            ApiBaseUrl = wrongUrl,
            Timeout = 2,
            RetryCount = 0,
        });

        await Assert.ThrowsAnyAsync<Exception>(() => bad.Client.GetAsync<JsonElement>("/"));

        using var good = ApiClientFactory.Create(new TcBackendOptions
        {
            ApiBaseUrl = DefaultBaseUrl,
            Timeout = 15,
            RetryCount = 1,
        });

        var health = await good.Client.GetAsync<JsonElement>("/health");
        Assert.True(health.Success);
        Assert.Equal(DefaultBaseUrl.TrimEnd('/'), good.Client.ApiBaseUrl.TrimEnd('/'));
    }
}
