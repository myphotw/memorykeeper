using MemoryKeeper.App.Maps;
using MemoryKeeper.App.Maps.Google;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MemoryKeeper.App.Dialogs;

/// <summary>Read-only map preview shown before a place radius is expanded.</summary>
public static class PlaceRadiusExpansionDialog
{
    public static async Task<bool> ShowAsync(
        XamlRoot xamlRoot,
        ILoggerFactory loggerFactory,
        ISettingRepository settingRepository,
        string placeDisplayName,
        PlaceRadiusExpansionPlan plan)
    {
        var webView = new WebView2
        {
            Height = 330,
            MinWidth = 440,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var mapController = new GoogleMapController(
            webView,
            loggerFactory.CreateLogger<GoogleMapController>());

        var status = new TextBlock
        {
            Text = "지도 미리보기를 불러오는 중...",
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap,
        };
        var legend = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                BuildLegendItem("#1565C0", $"현재 범위 {plan.CurrentRadiusMeters:0}m"),
                BuildLegendItem("#F57C00", $"변경 예정 {plan.ProposedRadiusMeters:0}m"),
                BuildLegendItem("#C62828", "현재 범위 밖 사진"),
            },
        };
        var content = new StackPanel
        {
            Width = 520,
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = $"'{placeDisplayName}'의 범위를 넓혀야 선택한 사진의 장소 등록이 유지됩니다.",
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                legend,
                webView,
                new TextBlock
                {
                    Text = "범위를 넓히면 주변의 다른 사진도 이 장소로 자동 분류될 수 있습니다.",
                    FontSize = 12,
                    Opacity = 0.8,
                    TextWrapping = TextWrapping.Wrap,
                },
                status,
            },
        };
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "장소 범위 확인",
            PrimaryButtonText = "범위 늘리고 등록",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            Content = content,
        };
        using var initializationCts = new CancellationTokenSource();
        var initializationTask = Task.CompletedTask;

        async Task InitializeMapAsync()
        {
            try
            {
                var cancellationToken = initializationCts.Token;
                var apiKey = await MapDisplayCredentialProvider.GetAsync(settingRepository);
                await mapController.InitializeAsync(apiKey, cancellationToken);
                var markers = new List<MapMarker>
                {
                    new(
                        Guid.NewGuid(),
                        $"{placeDisplayName} 중심",
                        plan.CenterLatitude,
                        plan.CenterLongitude,
                        "장소 중심",
                        MapMarkerVisualState.Matched,
                        Scale: 1.1,
                        IsMatched: true),
                };
                markers.AddRange(plan.PhotoPoints.Select(point => new MapMarker(
                    point.MediaId,
                    point.FileName,
                    point.Latitude,
                    point.Longitude,
                    $"중심에서 {point.DistanceMeters:0}m",
                    point.IsOutsideCurrentRadius
                        ? MapMarkerVisualState.Selected
                        : MapMarkerVisualState.Matched,
                    IsMatched: !point.IsOutsideCurrentRadius)));

                await mapController.SetMarkersAsync(markers, cancellationToken);
                await mapController.SetRadiusPreviewAsync(new MapRadiusPreview(
                    plan.CenterLatitude,
                    plan.CenterLongitude,
                    plan.CurrentRadiusMeters,
                    plan.ProposedRadiusMeters), cancellationToken);
                status.Text = $"GPS 사진 {plan.PhotoPoints.Count}장 · 현재 범위 밖 {plan.PhotoPoints.Count(point => point.IsOutsideCurrentRadius)}장";
                dialog.IsPrimaryButtonEnabled = true;
            }
            catch (OperationCanceledException) when (initializationCts.IsCancellationRequested)
            {
                // The user closed the preview while its map was still initializing.
            }
            catch (Exception ex)
            {
                status.Text = $"지도 미리보기를 표시하지 못했습니다. {ex.Message}";
                dialog.IsPrimaryButtonEnabled = false;
            }
        }

        dialog.Opened += (_, _) => initializationTask = InitializeMapAsync();

        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            initializationCts.Cancel();
            await initializationTask;
            await mapController.DisposeAsync();
        }
    }

    private static FrameworkElement BuildLegendItem(string color, string text) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 5,
        Children =
        {
            new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    255,
                    Convert.ToByte(color[1..3], 16),
                    Convert.ToByte(color[3..5], 16),
                    Convert.ToByte(color[5..7], 16))),
            },
            new TextBlock { Text = text, FontSize = 12 },
        },
    };
}
