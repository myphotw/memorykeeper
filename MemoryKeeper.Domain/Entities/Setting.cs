namespace MemoryKeeper.Domain.Entities;

/// <summary>
/// Application-level configuration entry.
/// </summary>
public class Setting : BaseEntity
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
