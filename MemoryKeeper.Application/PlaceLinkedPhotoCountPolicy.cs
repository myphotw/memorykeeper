using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

public enum PlaceLinkedPhotoCountFilter
{
    All,
    HasPhotos,
    NoPhotos,
}

public static class PlaceLinkedPhotoCountPolicy
{
    public static IReadOnlyDictionary<Guid, int> Project(
        IEnumerable<MemoryKeeperPlaceHierarchyPathApiDto> hierarchyPaths) =>
        hierarchyPaths
            .Where(path => path.MemorykeeperPlaceId != Guid.Empty)
            .GroupBy(path => path.MemorykeeperPlaceId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .DistinctBy(PathIdentity, StringComparer.OrdinalIgnoreCase)
                    .Sum(path => Math.Max(0, path.PhotoCount)));

    public static bool Matches(int? linkedPhotoCount, PlaceLinkedPhotoCountFilter filter) => filter switch
    {
        PlaceLinkedPhotoCountFilter.HasPhotos => linkedPhotoCount is > 0,
        PlaceLinkedPhotoCountFilter.NoPhotos => linkedPhotoCount == 0,
        _ => true,
    };

    public static string Format(int? linkedPhotoCount) => linkedPhotoCount.HasValue
        ? $"사진 {linkedPhotoCount.Value:N0}장"
        : "사진 수 확인 불가";

    private static string PathIdentity(MemoryKeeperPlaceHierarchyPathApiDto path) =>
        !string.IsNullOrWhiteSpace(path.LocationKey)
            ? path.LocationKey.Trim()
            : !string.IsNullOrWhiteSpace(path.FullPath)
                ? path.FullPath.Trim()
                : string.Join("|", path.Country?.Trim(), path.Region?.Trim(), path.DisplayName?.Trim());
}
