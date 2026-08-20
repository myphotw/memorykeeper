using MemoryKeeper.App.Maps.Google;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace MemoryKeeper.App.Dialogs;

/// <summary>
/// Map pin picker that runs inside an already-open <see cref="ContentDialog"/>.
/// WinUI allows only one ContentDialog at a time, so nested ShowAsync is not used.
/// </summary>
public static class MapPickSession
{
    public sealed class SearchHooks
    {
        public required Func<string, Task<IReadOnlyList<PlaceSuggestionDto>>> SearchAsync { get; init; }

        public required Func<PlaceSuggestionDto, Task<(double Latitude, double Longitude)?>> ResolveCoordinatesAsync { get; init; }
    }

    public static async Task<bool> RunInDialogAsync(
        ContentDialog host,
        ILoggerFactory loggerFactory,
        ISettingRepository settingRepository,
        double initialLatitude,
        double initialLongitude,
        double initialRadiusMeters,
        Func<double, double, double, Task<string>> applyAsync,
        Action discardSelection,
        SearchHooks? searchHooks = null)
    {
        var originalContent = host.Content;
        var originalTitle = host.Title;
        var originalPrimary = host.PrimaryButtonText;
        var originalClose = host.CloseButtonText;
        var originalDefault = host.DefaultButton;
        var originalPrimaryEnabled = host.IsPrimaryButtonEnabled;

        var currentLat = initialLatitude;
        var currentLng = initialLongitude;

        var webView = new WebView2
        {
            Height = 280,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 440
        };
        var radiusBox = new NumberBox
        {
            Header = "반경 (m)",
            Value = initialRadiusMeters,
            Minimum = 20,
            Maximum = 2000,
            SmallChange = 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        var status = new TextBlock
        {
            Text = "핀을 드래그하거나 지도를 클릭하세요.",
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        };

        var mapController = new GoogleMapController(
            webView,
            loggerFactory.CreateLogger<GoogleMapController>());

        async Task ApplyPointAsync(double lat, double lng)
        {
            currentLat = lat;
            currentLng = lng;
            status.Text = await applyAsync(lat, lng, radiusBox.Value);
        }

        EventHandler<(double Lat, double Lng)> dragHandler = (_, point) =>
            _ = ApplyPointAsync(point.Lat, point.Lng);

        EventHandler<(double Lat, double Lng)> clickHandler = async (_, point) =>
        {
            await ApplyPointAsync(point.Lat, point.Lng);
            await mapController.SetEditablePinAsync(point.Lat, point.Lng, radiusBox.Value);
        };

        mapController.EditableMarkerDragEnded += dragHandler;
        mapController.MapClicked += clickHandler;

        radiusBox.ValueChanged += async (_, args) =>
        {
            if (double.IsNaN(args.NewValue))
            {
                return;
            }

            await mapController.UpdateEditableRadiusAsync(args.NewValue);
            status.Text = await applyAsync(currentLat, currentLng, args.NewValue);
        };

        var content = new StackPanel
        {
            Spacing = 8,
            Width = 480
        };

        content.Children.Add(new TextBlock
        {
            Text = "검색으로 장소를 찾거나, 지도를 클릭·핀을 드래그해 위치를 지정하세요.",
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });

        if (searchHooks is not null)
        {
            var searchBox = new TextBox
            {
                PlaceholderText = "장소 검색 (예: 오사카성, 성수동 카페)",
                MinHeight = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var resultsList = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 120,
                Visibility = Visibility.Collapsed,
                ItemTemplate = (DataTemplate)XamlReader.Load(
                    """
                    <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                      <StackPanel Spacing="2" Padding="4,6">
                        <TextBlock Text="{Binding PrimaryText}" FontWeight="SemiBold" TextWrapping="Wrap" />
                        <TextBlock Text="{Binding SecondaryText}" Opacity="0.7" TextWrapping="Wrap" FontSize="12" />
                      </StackPanel>
                    </DataTemplate>
                    """)
            };

            var resultsPanel = new Border
            {
                Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["LayerOnAcrylicFillColorDefaultBrush"],
                BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"],
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 8, 8, 4),
                Visibility = Visibility.Collapsed,
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "검색 결과",
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 12,
                            Margin = new Thickness(4, 0, 4, 2)
                        },
                        resultsList
                    }
                }
            };

            var searchCts = new CancellationTokenSource();
            searchBox.TextChanged += async (_, _) =>
            {
                searchCts.Cancel();
                searchCts = new CancellationTokenSource();
                var token = searchCts.Token;
                var query = searchBox.Text?.Trim() ?? string.Empty;
                if (query.Length < 2)
                {
                    resultsPanel.Visibility = Visibility.Collapsed;
                    resultsList.ItemsSource = null;
                    return;
                }

                try
                {
                    await Task.Delay(280, token);
                    var results = await searchHooks.SearchAsync(query);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    resultsList.ItemsSource = results;
                    resultsPanel.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    if (results.Count == 0)
                    {
                        status.Text = "검색 결과가 없습니다.";
                    }
                }
                catch (TaskCanceledException)
                {
                    // superseded by newer keystroke
                }
                catch (Exception ex)
                {
                    status.Text = ex.Message;
                }
            };

            resultsList.SelectionChanged += async (_, _) =>
            {
                if (resultsList.SelectedItem is not PlaceSuggestionDto suggestion)
                {
                    return;
                }

                status.Text = $"'{suggestion.PrimaryText}' 좌표를 가져오는 중…";
                try
                {
                    var coords = await searchHooks.ResolveCoordinatesAsync(suggestion);
                    if (coords is null)
                    {
                        status.Text = $"'{suggestion.PrimaryText}' 좌표를 가져오지 못했습니다.";
                        return;
                    }

                    await ApplyPointAsync(coords.Value.Latitude, coords.Value.Longitude);
                    await mapController.SetEditablePinAsync(
                        coords.Value.Latitude,
                        coords.Value.Longitude,
                        radiusBox.Value);
                    status.Text =
                        $"검색 위치: {suggestion.PrimaryText} · {coords.Value.Latitude:F6}, {coords.Value.Longitude:F6}";
                    resultsPanel.Visibility = Visibility.Collapsed;
                    searchBox.Text = suggestion.PrimaryText;
                }
                catch (Exception ex)
                {
                    status.Text = ex.Message;
                }
            };

            content.Children.Add(searchBox);
            content.Children.Add(resultsPanel);
        }

        content.Children.Add(webView);
        content.Children.Add(radiusBox);
        content.Children.Add(status);

        host.Content = content;
        host.Title = "지도에서 선택";
        host.PrimaryButtonText = "핀 확정";
        host.CloseButtonText = "뒤로";
        host.DefaultButton = ContentDialogButton.Primary;
        host.IsPrimaryButtonEnabled = true;

        var completion = new TaskCompletionSource<bool>();

        void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs e)
        {
            e.Cancel = true;
            sender.PrimaryButtonClick -= OnPrimaryClick;
            sender.CloseButtonClick -= OnCloseClick;
            completion.TrySetResult(true);
        }

        void OnCloseClick(ContentDialog sender, ContentDialogButtonClickEventArgs e)
        {
            e.Cancel = true;
            sender.PrimaryButtonClick -= OnPrimaryClick;
            sender.CloseButtonClick -= OnCloseClick;
            completion.TrySetResult(false);
        }

        host.PrimaryButtonClick += OnPrimaryClick;
        host.CloseButtonClick += OnCloseClick;

        try
        {
            var apiKey = await MapDisplayCredentialProvider.GetAsync(settingRepository);
            await mapController.InitializeAsync(apiKey);
            await mapController.EnableMapClickAsync(true);
            await mapController.SetEditablePinAsync(initialLatitude, initialLongitude, radiusBox.Value);
            status.Text = await applyAsync(initialLatitude, initialLongitude, radiusBox.Value);
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
        }

        var confirmed = await completion.Task;

        mapController.EditableMarkerDragEnded -= dragHandler;
        mapController.MapClicked -= clickHandler;
        await mapController.DisposeAsync();

        host.Content = originalContent;
        host.Title = originalTitle;
        host.PrimaryButtonText = originalPrimary;
        host.CloseButtonText = originalClose;
        host.DefaultButton = originalDefault;
        host.IsPrimaryButtonEnabled = originalPrimaryEnabled;

        if (!confirmed)
        {
            discardSelection();
        }

        return confirmed;
    }
}
