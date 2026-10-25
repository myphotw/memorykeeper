using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application;

public static class PlaceHierarchyChangePolicy
{
    public static bool HasCanonicalGeographyChanged(PlaceDto original, UpdatePlaceRequest update)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(update);

        return !Same(original.DisplayName, update.DisplayName)
               || !Same(original.Country, update.Country)
               || !Same(original.Province, update.Province)
               || !Same(original.City, update.City)
               || !Same(original.District, update.District);
    }

    public static string BuildImpactMessage(string displayName, int photoCount) =>
        $"등록 장소 '{displayName}'의 이름·국가·지역 정보를 수정합니다. "
        + $"이 장소를 사용하는 사진 {Math.Max(0, photoCount):N0}장의 사진첩 위치 경로와 집계가 바뀔 수 있습니다. "
        + "사진 원본의 GPS·EXIF·raw metadata는 변경되지 않습니다. 계속하시겠습니까?";

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
