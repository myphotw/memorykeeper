namespace MemoryKeeper.Domain.Entities;

/// <summary>
/// Common audit fields shared by all domain entities.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// Time when the entity was registered in Memory Keeper DB.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Time when the entity information was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
