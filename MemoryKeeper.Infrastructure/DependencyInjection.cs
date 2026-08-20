using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Interfaces;
using MemoryKeeper.Infrastructure.Import;
using MemoryKeeper.Infrastructure.Location;
using MemoryKeeper.Infrastructure.Metadata;
using MemoryKeeper.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MemoryKeeper.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<IStorageProvider, LocalStorageProvider>();
        services.AddSingleton<IFileAccessService, LocalFileAccessService>();
        services.AddSingleton<IFileScanner, FileScanner>();
        services.AddSingleton<IFileHasher, FileHasher>();
        services.AddSingleton<IFileStorageService, FileStorageService>();
        services.AddSingleton<IMetadataExtractor, MetadataExtractorService>();

        services.AddTransient<ILocationResolver, TcBackendLocationResolver>();

        return services;
    }
}
