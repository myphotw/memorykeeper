using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;
using Microsoft.UI.Xaml;
using System.Text;

namespace MemoryKeeper.App.Services;

public static class PlaceOverlapPrompt
{
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

        var message = BuildMessage(overlaps, impact);
        return await UserFeedback.ConfirmAsync(xamlRoot, "장소 반경 겹침", message, "진행", "취소");
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
}
