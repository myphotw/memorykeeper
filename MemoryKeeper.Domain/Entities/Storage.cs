using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Domain.Entities;

/// <summary>
/// Physical or remote storage location for library files.
/// </summary>
public class Storage : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public StorageType StorageType { get; set; }

    /// <summary>
    /// Photo library root (local path, external drive, or NAS path).
    /// Absolute file path = PhotoRoot + RelativePath.
    /// </summary>
    public string PhotoRoot { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public ICollection<Media> MediaItems { get; set; } = new List<Media>();
}
