using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Database;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MemoryKeeper.Infrastructure.Database;

public static class DependencyInjection
{
    public static IServiceCollection AddMemoryKeeperDatabase(
        this IServiceCollection services,
        string? databaseDirectory = null)
    {
        var connectionString = SqliteConnectionFactory.CreateConnectionString(databaseDirectory);

        services.AddDbContext<MemoryKeeperDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IMediaRepository, MediaRepository>();
        services.AddScoped<IStorageRepository, StorageRepository>();
        services.AddScoped<ISettingRepository, SettingRepository>();
        services.AddScoped<IPlaceRepository, PlaceRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IMediaTagRepository, MediaTagRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        // V2: travel aggregates from Gallery API (SQLite TravelRecordsRepository unused).
        services.AddScoped<ITravelRecordsRepository, MemoryKeeper.Infrastructure.Repositories.Api.GalleryTravelRecordsRepository>();

        return services;
    }
}
