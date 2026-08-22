using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;
using Microsoft.UI.Xaml;
using System.Text;

namespace MemoryKeeper.App.Services;

public static class PlaceOverlapPrompt
{
    /// <summary>Legacy local flow retained for screens not yet moved to the NAS Place domain.</summary>
    public static async Task<bool> ConfirmIfNeededAsync(
        XamlRoot? xamlRoot,
        PlaceService placeService,
        double latitude,
        double longitude,
        double radiusMeters,
        Guid? excludePlaceId = null,
        CancellationToken cancellationToken = default)
    {
        var overlaps = await placeService.FindOverlappingPlacesAsync(
            latitude,
            longitude,
            radiusMeters,
            excludePlaceId,
            cancellationToken);
        if (overlaps.Count == 0)
        {
            return true;
        }

        var impact = await placeService.CountRadiusImpactAsync(
            latitude,
            longitude,
            radiusMeters,
            excludePlaceId,
            cancellationToken);
        return await UserFeedback.ConfirmAsync(
            xamlRoot,
            "장소 반경 겹침",
            BuildMessage(overlaps, impact),
            "진행",
            "취소");
    }

    public static async Task<bool> ConfirmIfNeededAsync(
        XamlRoot? xamlRoot,
        MemoryKeeperPlaceService placeService,
        string targetDisplayName,
        double latitude,
        double longitude,
        double radiusMeters,
        Guid? excludePlaceId = null,
        CancellationToken cancellationToken = default)
    {
        var backendImpact = await placeService.GetRadiusImpactAsync(
            latitude,
            longitude,
            radiusMeters,
            excludePlaceId,
            cancellationToken);

        return await ConfirmImpactIfNeededAsync(
            xamlRoot,
            targetDisplayName,
            backendImpact,
            cancellationToken);
    }

    public static async Task<bool> ConfirmImpactIfNeededAsync(
        XamlRoot? xamlRoot,
        string targetDisplayName,
        MemoryKeeperRadiusImpactApiResult impact,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (impact.OverlappingPlaces.Count == 0)
        {
            return true;
        }

        return await UserFeedback.ConfirmAsync(
            xamlRoot,
            "장소 반경 겹침",
            BuildNasMessage(targetDisplayName, impact),
            "확인",
            "취소");
    }

    public static string BuildNasMessage(
        string targetDisplayName,
        MemoryKeeperRadiusImpactApiResult impact)
    {
        var displayName = string.IsNullOrWhiteSpace(targetDisplayName)
            ? "현재 장소"
            : $"'{targetDisplayName.Trim()}'";
        var builder = new StringBuilder();
        builder.AppendLine("수정한 범위가 다음 등록 장소와 겹칩니다.");
        builder.AppendLine();
        foreach (var overlap in impact.OverlappingPlaces)
        {
            builder.AppendLine(
                $"· {overlap.Place.DisplayName} · 중심 거리 {FormatDistance(overlap.CenterDistanceM)}" +
                $" · 반경 {overlap.Place.RadiusM:0}m · 연결 사진 {overlap.Place.UsageCount}장");
        }

        builder.AppendLine();
        builder.AppendLine(
            $"계속하면 새 범위 안의 사진 {impact.MatchedFileCount}장이 {displayName}(으)로 재분류될 수 있습니다.");
        builder.Append("실제 이동 사진은 저장 후 각 사진의 원본 위치를 기준으로 결정됩니다. 계속하시겠습니까?");
        return builder.ToString();
    }

    public static string BuildMessage(
        IReadOnlyList<PlaceOverlapItemDto> overlaps,
        PlaceRadiusImpactDto impact)
    {
        var builder = new StringBuilder();
        builder.AppendLine("다음 장소와 반경이 겹칩니다.");
        builder.AppendLine();
        foreach (var item in overlaps)
        {
            builder.AppendLine($"· {item.SummaryText}");
        }

        builder.AppendLine();
        builder.AppendLine(
            $"진행하면 이 좌표 범위의 사진 {impact.TotalInRadius}장" +
            $" (미등록 {impact.UnassignedCount}장 · 다른 장소 {impact.FromOtherPlacesCount}장)이" +
            " 현재 장소로 연결됩니다.");
        builder.Append("계속하시겠습니까?");
        return builder.ToString();
    }

    private static string FormatDistance(double meters) =>
        meters < 1000 ? $"{Math.Round(meters)}m" : $"{meters / 1000d:0.0}km";
}
