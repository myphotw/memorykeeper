namespace MemoryKeeper.Infrastructure.Services.Api;

/// <summary>
/// Binding for the <c>TcBackend</c> section in appsettings.json.
/// </summary>
public sealed class TcBackendOptions
{
    public const string SectionName = "TcBackend";
    public const string ProductionApiBaseUrl = "https://onepieces.synology.me:8443";
    public const string ApiBaseUrlEnvironmentVariable = "TC_BACKEND_URL";
    public const string AuthTokenEnvironmentVariable = "TC_BACKEND_AUTH_TOKEN";

    public string ApiBaseUrl { get; set; } = ProductionApiBaseUrl;

    /// <summary>
    /// Deployment-provisioned Bearer token. Never put the real value in tracked configuration.
    /// </summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>HTTP timeout in seconds.</summary>
    public int Timeout { get; set; } = 30;

    public int RetryCount { get; set; } = 3;

    public string Version { get; set; } = "1.0.0";

    public string ServiceName { get; set; } = "MemoryKeeper";

    /// <summary>Max parallel HTTP uploads for Import (Phase 3B). Clamped to 1–3.</summary>
    public int MaxConcurrentUploads { get; set; } = 3;
}
