using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Dialogs;

public static class DirectPlaceAssignmentDialog
{
    public static async Task<PlaceDto?> ShowAsync(
        XamlRoot xamlRoot,
        IReadOnlyCollection<PlaceDto> registeredPlaces,
        int selectedPhotoCount)
    {
        ArgumentNullException.ThrowIfNull(registeredPlaces);

        var allItems = registeredPlaces
            .Where(place => place.IsActive && place.Id != Guid.Empty)
            .OrderByDescending(place => place.IsFavorite)
            .ThenByDescending(place => place.LastUsedAt)
            .ThenBy(place => place.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(place => new DirectPlaceItem(place))
            .ToList();
        var searchBox = new TextBox
        {
            Header = "등록된 장소 검색",
            PlaceholderText = "장소 이름 또는 지역",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var placeList = new ListView
        {
            ItemsSource = allItems,
            DisplayMemberPath = nameof(DirectPlaceItem.DisplayText),
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 360,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var emptyText = new TextBlock
        {
            Text = allItems.Count == 0 ? "등록된 장소가 없습니다." : "검색 결과가 없습니다.",
            TextWrapping = TextWrapping.Wrap,
            Visibility = allItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed,
            Opacity = 0.75,
        };
        var content = new StackPanel { Spacing = 12, MinWidth = 400 };
        content.Children.Add(new TextBlock
        {
            Text = $"{selectedPhotoCount:N0}장 선택",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        content.Children.Add(new TextBlock
        {
            Text = "선택한 사진을 GPS 위치와 관계없이 이 장소로 분류합니다. 장소 범위는 변경되지 않습니다.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });
        content.Children.Add(searchBox);
        content.Children.Add(placeList);
        content.Children.Add(emptyText);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "장소 직접 지정",
            Content = content,
            PrimaryButtonText = "장소 지정",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };

        placeList.SelectionChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = placeList.SelectedItem is DirectPlaceItem;
        searchBox.TextChanged += (_, _) =>
        {
            var term = searchBox.Text?.Trim();
            var filtered = string.IsNullOrWhiteSpace(term)
                ? allItems
                : allItems.Where(item => item.Matches(term)).ToList();
            placeList.ItemsSource = filtered;
            placeList.SelectedItem = null;
            emptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || placeList.SelectedItem is not DirectPlaceItem selected)
        {
            return null;
        }

        return selected.Place;
    }

    private sealed class DirectPlaceItem(PlaceDto place)
    {
        public PlaceDto Place { get; } = place;

        public string DisplayText { get; } = string.IsNullOrWhiteSpace(place.RegionSummary)
            ? place.DisplayName
            : $"{place.DisplayName} · {place.RegionSummary}";

        public bool Matches(string term) =>
            DisplayText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || Place.Address.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (Place.CanonicalName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
