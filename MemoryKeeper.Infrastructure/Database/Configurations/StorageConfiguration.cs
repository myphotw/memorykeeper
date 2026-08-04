using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Infrastructure.Database.Configurations;

public sealed class StorageConfiguration : IEntityTypeConfiguration<StorageEntity>
{
    public void Configure(EntityTypeBuilder<StorageEntity> builder)
    {
        builder.ToTable("TB_STORAGE");

        builder.HasKey(storage => storage.Id);

        builder.Property(storage => storage.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(storage => storage.StorageType)
            .IsRequired();

        builder.Property(storage => storage.PhotoRoot)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(storage => storage.IsActive)
            .IsRequired();

        builder.Property(storage => storage.CreatedAt)
            .IsRequired();

        builder.Property(storage => storage.UpdatedAt)
            .IsRequired();

        builder.HasIndex(storage => storage.Name)
            .IsUnique();

        builder.HasIndex(storage => storage.PhotoRoot);
    }
}
