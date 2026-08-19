using System.Text.Json.Serialization;

namespace MemoryKeeper.Infrastructure.Services.Api.Models;

public sealed class BackendHealthDto
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}

public sealed class BackendProviderReadinessDto
{
    [JsonPropertyName("configured")]
    public bool Configured { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

public sealed class BackendVisionReadinessDto
{
    [JsonPropertyName("credential_available")]
    public bool CredentialAvailable { get; init; }

    [JsonPropertyName("worker_running")]
    public bool WorkerRunning { get; init; }

    [JsonPropertyName("worker_status")]
    public string WorkerStatus { get; init; } = string.Empty;
}

public sealed class BackendReadinessDto
{
    [JsonPropertyName("services")]
    public IReadOnlyDictionary<string, BackendProviderReadinessDto> Services { get; init; }
        = new Dictionary<string, BackendProviderReadinessDto>(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("vision")]
    public BackendVisionReadinessDto Vision { get; init; } = new();
}

public sealed class BackendCapabilitiesDto
{
    [JsonPropertyName("api_version")]
    public string ApiVersion { get; init; } = string.Empty;

    [JsonPropertyName("service_version")]
    public string ServiceVersion { get; init; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public IReadOnlyDictionary<string, bool> Capabilities { get; init; }
        = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("supported_services")]
    public IReadOnlyList<string> SupportedServices { get; init; } = [];
}

public sealed class BackendConnectionSnapshot
{
    public BackendHealthDto? Health { get; init; }

    public BackendReadinessDto? Readiness { get; init; }

    public BackendCapabilitiesDto? Capabilities { get; init; }

    public ApiErrorCategory? ErrorCategory { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsConnected => Health is not null
        && string.Equals(Health.Status, "ok", StringComparison.OrdinalIgnoreCase)
        && Readiness is not null
        && Capabilities is not null
        && ErrorCategory is null;
}
