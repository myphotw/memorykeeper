using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Infrastructure.Database.Configurations;
using Microsoft.EntityFrameworkCore;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Infrastructure.Database;

public sealed class MemoryKeeperDbContext : DbContext
{
    public MemoryKeeperDbContext(DbContextOptions<MemoryKeeperDbContext> options)
        : base(options)
    {
    }

    public DbSet<Media> Media => Set<Media>();

    public DbSet<StorageEntity> Storages => Set<StorageEntity>();

    public DbSet<Place> Places => Set<Place>();

    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<MediaTag> MediaTags => Set<MediaTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new MediaConfiguration());
        modelBuilder.ApplyConfiguration(new StorageConfiguration());
        modelBuilder.ApplyConfiguration(new PlaceConfiguration());
        modelBuilder.ApplyConfiguration(new SettingConfiguration());
        modelBuilder.ApplyConfiguration(new TagConfiguration());
        modelBuilder.ApplyConfiguration(new MediaTagConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
