using MemoryKeeper.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MemoryKeeper.Infrastructure.Database.Configurations;

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("TB_TAG");

        builder.HasKey(tag => tag.Id);

        builder.Property(tag => tag.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(tag => tag.Color)
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(tag => tag.UsageCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(tag => tag.Source)
            .IsRequired()
            .HasDefaultValue(Domain.Enums.TagSource.User);

        builder.Property(tag => tag.IsPinned)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(tag => tag.CreatedAt)
            .IsRequired();

        builder.Property(tag => tag.UpdatedAt)
            .IsRequired();

        builder.HasIndex(tag => tag.Name)
            .IsUnique();

        builder.HasIndex(tag => tag.UsageCount);
        builder.HasIndex(tag => tag.Source);
        builder.HasIndex(tag => tag.IsPinned);
    }
}
