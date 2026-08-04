namespace MemoryKeeper.Domain.Enums;

/// <summary>
/// Distinguishes user-created tags from future AI-generated tags.
/// </summary>
public enum TagSource
{
    User = 0,
    Ai = 1
}
