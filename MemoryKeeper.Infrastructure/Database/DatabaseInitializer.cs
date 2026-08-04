using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Infrastructure.Database;

public sealed class DatabaseInitializationResult
{
    public required string DatabasePath { get; init; }

    public required bool WasCreated { get; init; }

    public required int MediaCount { get; init; }

    public required int StorageCount { get; init; }

    public required int SettingCount { get; init; }

    public string Summary =>
        $"DB ready at '{DatabasePath}'. Media={MediaCount}, Storage={StorageCount}, Setting={SettingCount}.";
}

public static class DatabaseInitializer
{
    public static async Task<DatabaseInitializationResult> InitializeAsync(
        IServiceProvider serviceProvider,
        string databaseDirectory,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("MemoryKeeper.Database");
        var dbContext = services.GetRequiredService<MemoryKeeperDbContext>();

        var databasePath = Path.Combine(databaseDirectory, SqliteConnectionFactory.DatabaseFileName);
        var wasCreated = !File.Exists(databasePath);

        logger?.LogInformation("Applying EF Core migrations. DatabasePath={DatabasePath}", databasePath);
        await dbContext.Database.MigrateAsync(cancellationToken);

        var fileAccessService = services.GetRequiredService<IFileAccessService>();
        await RelativePathDataMigrator.NormalizeAsync(
            dbContext,
            fileAccessService,
            logger,
            cancellationToken);

        var mediaCount = await dbContext.Media.CountAsync(cancellationToken);
        var storageCount = await dbContext.Storages.CountAsync(cancellationToken);
        var settingCount = await dbContext.Settings.CountAsync(cancellationToken);

        var result = new DatabaseInitializationResult
        {
            DatabasePath = databasePath,
            WasCreated = wasCreated,
            MediaCount = mediaCount,
            StorageCount = storageCount,
            SettingCount = settingCount
        };

        logger?.LogInformation(
            "Database initialized. Created={WasCreated}, Media={MediaCount}, Storage={StorageCount}, Setting={SettingCount}, Path={DatabasePath}",
            result.WasCreated,
            result.MediaCount,
            result.StorageCount,
            result.SettingCount,
            result.DatabasePath);

        return result;
    }
}
