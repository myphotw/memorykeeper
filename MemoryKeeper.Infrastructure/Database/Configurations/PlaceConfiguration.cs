using MemoryKeeper.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MemoryKeeper.Infrastructure.Database.Configurations;

public sealed class PlaceConfiguration : IEntityTypeConfiguration<Place>
{
    public void Configure(EntityTypeBuilder<Place> builder)
    {
        builder.ToTable("TB_PLACE");

        builder.HasKey(place => place.Id);

        builder.Property(place => place.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(place => place.Country)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(place => place.Province)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(place => place.City)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(place => place.Address)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(place => place.PostalCode)
            .IsRequired()
            .HasMaxLength(32)
            .HasDefaultValue(string.Empty);

        builder.Property(place => place.GooglePlaceId)
            .HasMaxLength(128);

        builder.Property(place => place.CanonicalName)
            .HasMaxLength(200);

        builder.Property(place => place.Category)
            .HasMaxLength(64);

        builder.HasIndex(place => place.GooglePlaceId);

        builder.Property(place => place.Latitude)
            .IsRequired();

        builder.Property(place => place.Longitude)
            .IsRequired();

        builder.Property(place => place.Radius)
            .IsRequired();

        builder.Property(place => place.IsActive)
            .IsRequired();

        builder.Property(place => place.IsFavorite)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(place => place.UsageCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(place => place.LastUsedAt);

        builder.Property(place => place.CreatedAt)
            .IsRequired();

        builder.Property(place => place.UpdatedAt)
            .IsRequired();

        builder.HasIndex(place => place.DisplayName);
        builder.HasIndex(place => new { place.Latitude, place.Longitude });
        builder.HasIndex(place => place.IsActive);
        builder.HasIndex(place => place.IsFavorite);
        builder.HasIndex(place => place.LastUsedAt);
    }
}
