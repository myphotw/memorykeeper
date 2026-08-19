using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Infrastructure.Services.Api;

/// <summary>
/// DI registration for TC-Backend <see cref="BaseApiClient"/> (singleton) backed by a named HttpClient.
/// </summary>
public static class ApiClientFactory
{
    public static IServiceCollection AddTcBackendApiClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TcBackendOptions>()
            .Bind(configuration.GetSection(TcBackendOptions.SectionName))
            .PostConfigure(ApplyDeploymentOverrides);
        return AddTcBackendApiClientCore(services);
    }

    public static IServiceCollection AddTcBackendApiClient(
        this IServiceCollection services,
        Action<TcBackendOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.PostConfigure<TcBackendOptions>(ApplyDeploymentOverrides);
        return AddTcBackendApiClientCore(services);
    }

    private static IServiceCollection AddTcBackendApiClientCore(IServiceCollection services)
    {
        services.AddTransient<TcBackendAuthenticationHandler>();
        services.AddHttpClient(BaseApiClient.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TcBackendOptions>>().Value;
            var timeoutSeconds = options.Timeout > 0 ? options.Timeout : 30;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }).AddHttpMessageHandler<TcBackendAuthenticationHandler>();

        services.AddSingleton<BaseApiClient>();
        services.AddSingleton<BackendConnectionService>();
        services.AddSingleton<BackendMediaDownloader>();
        return services;
    }

    /// <summary>
    /// Builds a disposable DI scope that exposes <see cref="BaseApiClient"/> for smoke tests / tooling.
    /// </summary>
    public static ApiClientHandle Create(TcBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTcBackendApiClient(o =>
        {
            o.ApiBaseUrl = options.ApiBaseUrl;
            o.AuthToken = options.AuthToken;
            o.Timeout = options.Timeout;
            o.RetryCount = options.RetryCount;
            o.Version = options.Version;
            o.ServiceName = options.ServiceName;
        });

        var provider = services.BuildServiceProvider();
        return new ApiClientHandle(provider, provider.GetRequiredService<BaseApiClient>());
    }

    private static void ApplyDeploymentOverrides(TcBackendOptions options)
    {
        var baseUrl = Environment.GetEnvironmentVariable(
            TcBackendOptions.ApiBaseUrlEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            options.ApiBaseUrl = baseUrl.Trim();
        }

        var authToken = Environment.GetEnvironmentVariable(
            TcBackendOptions.AuthTokenEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            options.AuthToken = authToken.Trim();
        }
    }

    public sealed class ApiClientHandle : IDisposable
    {
        private readonly ServiceProvider _provider;

        public ApiClientHandle(ServiceProvider provider, BaseApiClient client)
        {
            _provider = provider;
            Client = client;
        }

        public BaseApiClient Client { get; }

        public void Dispose() => _provider.Dispose();
    }
}
