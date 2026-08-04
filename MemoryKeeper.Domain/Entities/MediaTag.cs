namespace MemoryKeeper.Domain.Entities;

/// <summary>
/// Junction between Media and Tag. Not stored on Media columns.
/// </summary>
public class MediaTag : BaseEntity
{
    public Guid MediaId { get; set; }

    public Media? Media { get; set; }

    public Guid TagId { get; set; }

    public Tag? Tag { get; set; }
}
