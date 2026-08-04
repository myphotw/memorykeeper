namespace MemoryKeeper.Application.DTOs;

public sealed class GalleryTreeChildDto
{
    public string Title { get; init; } = string.Empty;

    public int Count { get; init; }

    public Guid? PlaceId { get; init; }

    public bool IsUnclassified { get; init; }

    public string? PlaceType { get; init; }

    public string? Icon { get; init; }

    /// <summary>Year under a place node (Place → Year browse mode).</summary>
    public int? Year { get; init; }
}

public sealed class GalleryHierarchyQuery
{
    public int? Year { get; init; }

    public string? Country { get; init; }

    /// <summary>
    /// City level (MK-042M: Year → Country → City → Place).
    /// </summary>
    public string? City { get; init; }

    public Guid? PlaceId { get; init; }

    public bool UnclassifiedOnly { get; init; }

    public string? SearchText { get; init; }

    public bool FavoritesOnly { get; init; }

    public bool RecentOnly { get; init; }

    public bool PendingOnly { get; init; }
}
