using MemoryKeeper.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MemoryKeeper.Infrastructure.Database.Configurations;

public sealed class MediaConfiguration : IEntityTypeConfiguration<Media>
{
    public void Configure(EntityTypeBuilder<Media> builder)
    {
        builder.ToTable("TB_MEDIA");

        builder.HasKey(media => media.Id);

        builder.Property(media => media.FileName)
            .IsRequired()
            .HasMaxLength(260);

        builder.Property(media => media.MediaType)
            .IsRequired();

        builder.Property(media => media.Status)
            .IsRequired();

        builder.Property(media => media.OriginalPath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(media => media.RelativePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(media => media.ContentHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(media => media.CapturedAt)
            .HasConversion(
                value => value.HasValue
                    ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("o")
                    : null,
                value => MediaQueryFilters.ParseUtcDateTime(value));

        builder.Property(media => media.DateTimeOriginal)
            .HasMaxLength(64);

        builder.Property(media => media.ImportedAt)
            .IsRequired()
            .HasConversion(
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("o"),
                value => MediaQueryFilters.ParseUtcDateTimeRequired(value));

        builder.Property(media => media.Latitude);

        builder.Property(media => media.Longitude);

        builder.Property(media => media.Altitude);

        builder.Property(media => media.Orientation);

        builder.Property(media => media.Width);

        builder.Property(media => media.Height);

        builder.Property(media => media.CameraMaker)
            .HasMaxLength(128);

        builder.Property(media => media.CameraModel)
            .HasMaxLength(128);

        builder.Property(media => media.Lens)
            .HasMaxLength(128);

        builder.Property(media => media.Iso)
            .HasMaxLength(32);

        builder.Property(media => media.Exposure)
            .HasMaxLength(64);

        builder.Property(media => media.FNumber)
            .HasMaxLength(32);

        builder.Property(media => media.FocalLength)
            .HasMaxLength(32);

        builder.Property(media => media.Memo)
            .HasMaxLength(4000);

        builder.Property(media => media.IsFavorite)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(media => media.CreatedAt)
            .IsRequired();

        builder.Property(media => media.UpdatedAt)
            .IsRequired();

        builder.HasIndex(media => media.ContentHash);
        builder.HasIndex(media => media.StorageId);
        builder.HasIndex(media => media.PlaceId);
        builder.HasIndex(media => media.CapturedAt);
        builder.HasIndex(media => media.IsFavorite);

        builder.HasOne(media => media.Storage)
            .WithMany(storage => storage.MediaItems)
            .HasForeignKey(media => media.StorageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(media => media.Place)
            .WithMany(place => place.MediaItems)
            .HasForeignKey(media => media.PlaceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
