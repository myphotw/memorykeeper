namespace MemoryKeeper.Infrastructure.Services.Api;

/// <summary>
/// Binding for the <c>TcBackend</c> section in appsettings.json.
/// </summary>
public sealed class TcBackendOptions
{
    public const string SectionName = "TcBackend";

    public string ApiBaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>HTTP timeout in seconds.</summary>
    public int Timeout { get; set; } = 30;

    public int RetryCount { get; set; } = 3;

    public string Version { get; set; } = "1.0.0";

    public string ServiceName { get; set; } = "MemoryKeeper";
}
