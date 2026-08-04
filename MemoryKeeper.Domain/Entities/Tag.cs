using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Domain.Entities;

/// <summary>
/// User (or future AI) tag applied to individual photos.
/// </summary>
public class Tag : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    /// <summary>
    /// Number of photos currently linked to this tag.
    /// </summary>
    public int UsageCount { get; set; }

    /// <summary>
    /// Reserved for future AI tags. UI currently uses User only.
    /// </summary>
    public TagSource Source { get; set; } = TagSource.User;

    /// <summary>
    /// When true, tag appears at the top of tag pickers.
    /// </summary>
    public bool IsPinned { get; set; }

    public ICollection<MediaTag> MediaTags { get; set; } = new List<MediaTag>();
}
