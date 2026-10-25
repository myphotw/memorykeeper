namespace MemoryKeeper.Application;

public static class LocationHierarchyDisplay
{
    public static string? GetWarning(bool? locationHierarchyComplete, string? cleanupReason)
    {
        if (locationHierarchyComplete is not false)
        {
            return null;
        }

        var reason = cleanupReason?.Trim().ToUpperInvariant();
        var message = reason switch
        {
            "PLACE_UNASSIGNED" => "등록된 장소가 없습니다.",
            "COUNTRY_MISSING" => "국가 정보가 부족합니다.",
            "REGION_MISSING" => "지역 정보가 부족합니다.",
            "PLACE_MISSING" => "장소 이름이 부족합니다.",
            _ => "사진첩 위치 계층 정보가 부족합니다.",
        };

        return $"{message} 사진 원본의 GPS·메타데이터는 변경되지 않습니다. 등록 장소가 있다면 설정 > 장소 관리에서 위치 정보를 확인하세요.";
    }

    public static string BuildCanonicalPath(string? country, string? region, string? place)
    {
        var parts = new[] { country, region, place }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
        return parts.Length == 0 ? "-" : string.Join(" > ", parts);
    }
}
