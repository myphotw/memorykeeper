using MemoryKeeper.Application;

namespace MemoryKeeper.Mobile.Models;

public sealed record MobileGalleryBrowseOption(
    string DisplayName,
    GalleryBrowseScope Scope,
    int? PhotoCount = null,
    string? Context = null,
    bool IsCurrent = false,
    string? GalleryTitle = null)
{
    private string CountLabel => PhotoCount is int count
        ? $"{DisplayName} · {count:N0}장"
        : DisplayName;

    public string Label => string.IsNullOrWhiteSpace(Context)
        ? CountLabel
        : $"{CountLabel} · {Context}";

    public string AccessibleLabel => Label;

    public string CountText => PhotoCount is int count ? $"{count:N0}장" : string.Empty;

    public string CurrentText => IsCurrent ? "현재" : string.Empty;

    public string PlaceContext
    {
        get
        {
            if (Scope is not GalleryBrowseScope.HierarchyScope { Year: int year }
                || string.IsNullOrWhiteSpace(Context))
            {
                return Context ?? string.Empty;
            }

            var yearPrefix = $"{year}년 · ";
            return Context.StartsWith(yearPrefix, StringComparison.Ordinal)
                ? Context[yearPrefix.Length..]
                : string.Equals(Context, $"{year}년", StringComparison.Ordinal)
                    ? string.Empty
                    : Context;
        }
    }
}
