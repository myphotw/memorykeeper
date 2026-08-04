using MemoryKeeper.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MemoryKeeper.Infrastructure.Database.Configurations;

public sealed class MediaTagConfiguration : IEntityTypeConfiguration<MediaTag>
{
    public void Configure(EntityTypeBuilder<MediaTag> builder)
    {
        builder.ToTable("TB_MEDIA_TAG");

        builder.HasKey(mediaTag => mediaTag.Id);

        builder.Property(mediaTag => mediaTag.MediaId)
            .IsRequired();

        builder.Property(mediaTag => mediaTag.TagId)
            .IsRequired();

        builder.Property(mediaTag => mediaTag.CreatedAt)
            .IsRequired();

        builder.Property(mediaTag => mediaTag.UpdatedAt)
            .IsRequired();

        builder.HasIndex(mediaTag => new { mediaTag.MediaId, mediaTag.TagId })
            .IsUnique();

        builder.HasIndex(mediaTag => mediaTag.TagId);
        builder.HasIndex(mediaTag => mediaTag.MediaId);

        builder.HasOne(mediaTag => mediaTag.Media)
            .WithMany()
            .HasForeignKey(mediaTag => mediaTag.MediaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(mediaTag => mediaTag.Tag)
            .WithMany(tag => tag.MediaTags)
            .HasForeignKey(mediaTag => mediaTag.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
