using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Infrastructure.Database;

/// <summary>
/// In-memory media sorting (SQLite-safe). Prefer after ToListAsync for DateTime fields.
/// </summary>
internal static class MediaSort
{
    public static List<Media> ByFavoriteThenCapturedDesc(IEnumerable<Media> items)
        => items
            .OrderByDescending(media => media.IsFavorite)
            .ThenByDescending(media => media.CapturedAt)
            .ThenByDescending(media => media.ImportedAt)
            .ToList();

    public static List<Media> ByCapturedDesc(IEnumerable<Media> items)
        => items
            .OrderByDescending(media => media.CapturedAt)
            .ThenByDescending(media => media.ImportedAt)
            .ToList();

    public static List<Media> ByCapturedAsc(IEnumerable<Media> items)
        => items
            .OrderBy(media => media.CapturedAt)
            .ThenBy(media => media.ImportedAt)
            .ThenBy(media => media.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static List<Media> ByImportedDesc(IEnumerable<Media> items)
        => items
            .OrderByDescending(media => media.ImportedAt)
            .ToList();

    public static List<Media> ByUpdatedThenImportedDesc(IEnumerable<Media> items)
        => items
            .OrderByDescending(media => media.UpdatedAt)
            .ThenByDescending(media => media.ImportedAt)
            .ToList();
}
