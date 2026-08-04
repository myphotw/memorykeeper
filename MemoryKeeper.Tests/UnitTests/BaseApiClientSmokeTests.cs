using System.Text.Json;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Tests.UnitTests;

/// <summary>
/// Smoke tests against a running TC-Backend (default http://localhost:8000).
/// Skips when the server is unreachable.
/// </summary>
public sealed class BaseApiClientSmokeTests
{
    private static readonly string DefaultBaseUrl =
        Environment.GetEnvironmentVariable("TC_BACKEND_URL") ?? "http://localhost:8000";

    [Fact]
    public async Task Get_Root_Health_Dashboard_Succeed()
    {
        if (!await IsServerReachableAsync(DefaultBaseUrl))
        {
            return; // skip when backend is down
        }

        using var handle = ApiClientFactory.Create(new TcBackendOptions
        {
            ApiBaseUrl = DefaultBaseUrl,
            Timeout = 15,
            RetryCount = 1,
            Version = "1.0.0",
            ServiceName = "MemoryKeeper",
        });

        var client = handle.Client;

        var root = await client.GetAsync<JsonElement>("/");
        Assert.True(root.Success);
        Assert.Equal(JsonValueKind.Object, root.Data.ValueKind);
        Assert.True(root.Data.TryGetProperty("service", out _) || root.Data.TryGetProperty("version", out _));

        var health = await client.GetAsync<JsonElement>("/api/common/health");
        Assert.True(health.Success);
        Assert.Equal(JsonValueKind.Object, health.Data.ValueKind);
        Assert.True(health.Data.TryGetProperty("status", out var status));
        Assert.False(string.IsNullOrWhiteSpace(status.GetString()));

        var dashboard = await client.GetAsync<JsonElement>("/api/common/dashboard");
        Assert.True(dashboard.Success);
        Assert.Equal(JsonValueKind.Object, dashboard.Data.ValueKind);
    }

    [Fact]
    public async Task ApiBaseUrl_Change_Is_Honored()
    {
        if (!await IsServerReachableAsync(DefaultBaseUrl))
        {
            return;
        }

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

        var root = await good.Client.GetAsync<JsonElement>("/");
        Assert.True(root.Success);
        Assert.Equal(DefaultBaseUrl.TrimEnd('/'), good.Client.ApiBaseUrl.TrimEnd('/'));
    }

    private static async Task<bool> IsServerReachableAsync(string baseUrl)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await http.GetAsync(baseUrl.TrimEnd('/') + "/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
