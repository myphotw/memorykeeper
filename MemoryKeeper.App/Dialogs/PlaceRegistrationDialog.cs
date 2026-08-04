using System.Collections.ObjectModel;
using System.Linq;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace MemoryKeeper.App.Dialogs;

public static class PlaceRegistrationDialog
{
    public sealed class Options
    {
        public string Title { get; init; } = "위치정보 추가/수정";

        public string PrimaryButtonText { get; init; } = "적용";

        public bool SupportsMapPick { get; init; }

        /// <summary>
        /// Invoked with the already-open place dialog so map pick can reuse it
        /// (WinUI allows only one ContentDialog).
        /// </summary>
        public Func<ContentDialog, Task>? MapPickHandler { get; init; }
    }

    public static async Task<bool> ShowAsync(
        XamlRoot xamlRoot,
        IPlaceRegistrationDialogViewModel viewModel,
        Options options)
    {
        await viewModel.PreparePlaceRegistrationAsync();

        var previewImage = new Image
        {
            Width = 88,
            Height = 88,
            Stretch = Stretch.UniformToFill,
            Source = viewModel.RegistrationPreviewImage
        };

        var gpsText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(viewModel.RegistrationGpsText)
                ? "❌ GPS 없음"
                : $"📍 GPS: {viewModel.RegistrationGpsText}",
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        };

        var fileNameText = new TextBlock
        {
            Text = viewModel.RegistrationPreviewFileName,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        var statusText = new TextBlock
        {
            Text = viewModel.PlaceDialogStatus,
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        };

        var previewCardHost = new Border();
        var comparisonHost = new Border { Visibility = Visibility.Collapsed };

        void RefreshPreviewUi(ContentDialog dialog)
        {
            previewCardHost.Child = BuildPreviewCard(viewModel.SelectedLocation);
            comparisonHost.Child = BuildComparisonPanel(viewModel);
            comparisonHost.Visibility = viewModel.ShowLocationChangeComparison
                ? Visibility.Visible
                : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = viewModel.CanApplyPlaceChange;
            statusText.Text = viewModel.PlaceDialogStatus;
            gpsText.Text = string.IsNullOrWhiteSpace(viewModel.RegistrationGpsText)
                ? "❌ GPS 없음"
                : $"📍 GPS: {viewModel.RegistrationGpsText}";
        }

        ContentDialog? dialogRef = null;

        void RequestRefresh()
        {
            if (dialogRef is not null)
            {
                RefreshPreviewUi(dialogRef);
            }
        }

        void OnPreviewChanged(object? _, EventArgs __) => RequestRefresh();

        viewModel.PlacePreviewChanged += OnPreviewChanged;

        var recentPanel = CreatePlaceChipPanel(
            viewModel.RecentPlaces,
            viewModel,
            RequestRefresh,
            "최근 사용한 장소가 없습니다.");
        var favoritePanel = CreateFavoritePlacePanel(viewModel, RequestRefresh);

        var existingSearchBox = new TextBox
        {
            PlaceholderText = "기존 장소 검색 (국가 · 지역 · 장소명)",
            Text = viewModel.ExistingPlaceSearchText,
            MinHeight = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var existingTree = CreateHierarchyTreeView(viewModel, RequestRefresh);
        var existingSearchResults = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 180,
            ItemsSource = viewModel.FilteredExistingPlaces,
            ItemTemplate = CreateExistingPlaceItemTemplate()
        };

        var googleSearchBox = new TextBox
        {
            PlaceholderText = "Google 장소 검색",
            Text = viewModel.PlaceSearchText,
            MinHeight = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var nearbyList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 120,
            ItemsSource = viewModel.NearbyCandidates,
            ItemTemplate = CreateNearbyItemTemplate()
        };

        var googleResults = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 160,
            ItemsSource = viewModel.PlaceSearchResults,
            ItemTemplate = CreateSuggestionItemTemplate()
        };

        var googleResultsHeader = new TextBlock
        {
            Text = "검색 결과",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(4, 0, 4, 6)
        };

        var googleResultsPanel = new Border
        {
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["LayerOnAcrylicFillColorDefaultBrush"],
            BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"],
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 8, 8, 4),
            Margin = new Thickness(0, 4, 0, 8),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                Spacing = 4,
                Children = { googleResultsHeader, googleResults }
            }
        };

        var existingResultsHeader = new TextBlock
        {
            Text = "기존 장소 검색 결과",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(4, 0, 4, 6)
        };

        var existingSearchResultsHost = new Border
        {
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["LayerOnAcrylicFillColorDefaultBrush"],
            BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"],
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 8, 8, 4),
            Margin = new Thickness(0, 4, 0, 8),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                Spacing = 4,
                Children = { existingResultsHeader, existingSearchResults }
            }
        };

        void ClearListSelections()
        {
            nearbyList.SelectedItem = null;
            googleResults.SelectedItem = null;
            existingSearchResults.SelectedItem = null;
        }

        void BringIntoView(FrameworkElement element)
        {
            element.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = true,
                VerticalOffset = 24
            });
        }

        existingSearchBox.TextChanged += async (_, _) =>
        {
            viewModel.ExistingPlaceSearchText = existingSearchBox.Text;
            await viewModel.SearchExistingPlacesAsync();
            var hasQuery = !string.IsNullOrWhiteSpace(viewModel.ExistingPlaceSearchText);
            existingTree.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;
            existingSearchResults.ItemsSource = viewModel.FilteredExistingPlaces;
            existingSearchResultsHost.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
            existingResultsHeader.Text = hasQuery
                ? $"기존 장소 검색 결과 ({viewModel.FilteredExistingPlaces.Count})"
                : "기존 장소 검색 결과";
            statusText.Text = viewModel.PlaceDialogStatus;
            if (hasQuery)
            {
                BringIntoView(existingSearchResultsHost);
            }
        };

        existingSearchResults.SelectionChanged += async (_, _) =>
        {
            if (existingSearchResults.SelectedItem is PlacePickerItemDto place)
            {
                await viewModel.SelectExistingPlaceAsync(place);
                ClearListSelections();
                RequestRefresh();
            }
        };

        nearbyList.SelectionChanged += async (_, _) =>
        {
            if (nearbyList.SelectedItem is NearbyPlaceCandidateDto candidate)
            {
                await viewModel.SelectNearbyCandidateAsync(candidate);
                googleResults.SelectedItem = null;
                existingSearchResults.SelectedItem = null;
                if (dialogRef is not null)
                {
                    RefreshPreviewUi(dialogRef);
                }
            }
        };

        googleResults.SelectionChanged += async (_, _) =>
        {
            if (googleResults.SelectedItem is PlaceSuggestionDto suggestion)
            {
                await viewModel.SelectGoogleSuggestionAsync(suggestion);
                nearbyList.SelectedItem = null;
                existingSearchResults.SelectedItem = null;
                if (dialogRef is not null)
                {
                    RefreshPreviewUi(dialogRef);
                }
            }
        };

        googleSearchBox.TextChanged += async (_, _) =>
        {
            viewModel.PlaceSearchText = googleSearchBox.Text;
            await viewModel.SearchPlaceSuggestionsAsync();
            googleResults.ItemsSource = viewModel.PlaceSearchResults;
            var hasResults = viewModel.PlaceSearchResults.Count > 0
                || (!string.IsNullOrWhiteSpace(viewModel.PlaceSearchText)
                    && viewModel.PlaceSearchText.Trim().Length >= 2);
            googleResultsPanel.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
            googleResultsHeader.Text = viewModel.PlaceSearchResults.Count > 0
                ? $"검색 결과 ({viewModel.PlaceSearchResults.Count})"
                : "검색 결과";
            statusText.Text = viewModel.PlaceDialogStatus;
            if (hasResults)
            {
                BringIntoView(googleResultsPanel);
            }
        };

        var photoHeaderPanel = new StackPanel { Spacing = 4 };
        photoHeaderPanel.Children.Add(fileNameText);
        photoHeaderPanel.Children.Add(gpsText);

        var previewBorder = new Border
        {
            Width = 88,
            Height = 88,
            CornerRadius = new CornerRadius(8),
            Child = previewImage
        };
        Grid.SetColumn(previewBorder, 0);
        photoHeaderPanel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(photoHeaderPanel, 1);

        var headerGrid = new Grid { ColumnSpacing = 12 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.Children.Add(previewBorder);
        headerGrid.Children.Add(photoHeaderPanel);

        // Preview + comparison scroll with the rest so the picker area stays usable.
        var scrollChildren = new List<UIElement>
        {
            previewCardHost,
            comparisonHost,
            CreateSectionLabel("최근 사용 장소"),
            recentPanel,
            CreateSectionLabel("즐겨찾기 장소"),
            favoritePanel,
            CreateSectionLabel("기존 장소 선택"),
            existingSearchBox,
            existingTree,
            existingSearchResultsHost,
            CreateGoogleSearchHeader(options, () => dialogRef, viewModel, ClearListSelections, RefreshPreviewUi, statusText),
            googleSearchBox,
            googleResultsPanel,
            CreateSectionLabel("주변 추천"),
            nearbyList
        };

        var scrollContent = new StackPanel
        {
            Spacing = 10,
            Padding = new Thickness(0, 0, 14, 0)
        };
        foreach (var child in scrollChildren)
        {
            scrollContent.Children.Add(child);
        }

        var scrollViewer = new ScrollViewer
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = scrollContent
        };

        var footer = new StackPanel { Spacing = 8 };
        footer.Children.Add(statusText);

        var rootPanel = new Grid
        {
            Width = 500,
            MaxWidth = 540,
            Height = 560,
            RowSpacing = 10
        };
        rootPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(headerGrid, 0);
        Grid.SetRow(scrollViewer, 1);
        Grid.SetRow(footer, 2);
        rootPanel.Children.Add(headerGrid);
        rootPanel.Children.Add(scrollViewer);
        rootPanel.Children.Add(footer);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = options.Title,
            PrimaryButtonText = options.PrimaryButtonText,
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            Content = rootPanel
        };
        dialogRef = dialog;
        RefreshPreviewUi(dialog);

        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                viewModel.CancelPlaceRegistration();
                return false;
            }

            return await viewModel.ConfirmPlaceRegistrationAsync();
        }
        finally
        {
            viewModel.PlacePreviewChanged -= OnPreviewChanged;
        }
    }

    private static FrameworkElement BuildPreviewCard(PlaceLocationPreview location)
    {
        var card = new Border
        {
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = "📍 선택된 장소",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13
        });

        if (location.IsEmpty)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "위치정보 없음 — 최근 장소 · 검색 · 지도에서 선택하세요.",
                Opacity = 0.7,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            card.Child = stack;
            return card;
        }

        stack.Children.Add(new TextBlock
        {
            Text = location.DisplayName,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap
        });

        var meta = string.Join(" · ", new[]
        {
            BlankToDash(location.Country),
            BlankToDash(location.Province),
            BlankToDash(location.City)
        }.Where(static part => part != "-"));

        if (!string.IsNullOrWhiteSpace(meta))
        {
            stack.Children.Add(new TextBlock
            {
                Text = meta,
                Opacity = 0.75,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = location.HasCoordinates
                ? $"{location.LatitudeText}, {location.LongitudeText} · {location.RadiusText}"
                : $"좌표 없음 · {location.RadiusText}",
            Opacity = 0.75,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        card.Child = stack;
        return card;
    }

    private static FrameworkElement BuildComparisonPanel(IPlaceRegistrationDialogViewModel viewModel)
    {
        if (!viewModel.ShowLocationChangeComparison)
        {
            return new Border();
        }

        var fromName = viewModel.OriginalLocation.IsEmpty
            ? "위치정보 없음"
            : viewModel.OriginalLocation.DisplayName;
        var toName = viewModel.SelectedLocation.DisplayName;

        var card = new Border
        {
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"],
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock
        {
            Text = "변경 내용",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Opacity = 0.85
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{fromName}  →  {toName}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        });
        card.Child = stack;
        return card;
    }

    private static string BlankToDash(string value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static TextBlock CreateSectionLabel(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 0)
        };

    private static FrameworkElement CreateGoogleSearchHeader(
        Options options,
        Func<ContentDialog?> getDialog,
        IPlaceRegistrationDialogViewModel viewModel,
        Action clearListSelections,
        Action<ContentDialog> refreshPreviewUi,
        TextBlock statusText)
    {
        var label = CreateSectionLabel("Google 장소 검색");
        label.Margin = new Thickness(0);

        if (!options.SupportsMapPick || options.MapPickHandler is null)
        {
            return label;
        }

        var header = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var mapLink = new HyperlinkButton
        {
            Content = "지도에서 직접 선택",
            Padding = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        mapLink.Click += async (_, _) =>
        {
            var dialog = getDialog();
            if (dialog is null || options.MapPickHandler is null)
            {
                return;
            }

            clearListSelections();
            viewModel.ClearExternalPlaceSelections();
            try
            {
                await options.MapPickHandler(dialog);
            }
            catch (Exception ex)
            {
                statusText.Text = ex.Message;
            }

            refreshPreviewUi(dialog);
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(mapLink, 1);
        header.Children.Add(label);
        header.Children.Add(mapLink);
        return header;
    }

    private static FrameworkElement CreatePlaceChipPanel(
        IReadOnlyList<PlacePickerItemDto> items,
        IPlaceRegistrationDialogViewModel viewModel,
        Action refresh,
        string emptyText)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        if (items.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = emptyText,
                Opacity = 0.7,
                FontSize = 12
            });
            return panel;
        }

        foreach (var place in items)
        {
            var button = new Button
            {
                Content = place.DisplayName,
                MinHeight = 36,
                Padding = new Thickness(12, 6, 12, 6)
            };
            button.Click += async (_, _) =>
            {
                await viewModel.SelectExistingPlaceAsync(place);
                refresh();
            };
            panel.Children.Add(button);
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            // Bottom: horizontal scrollbar; Right: parent dialog vertical scrollbar.
            Padding = new Thickness(0, 2, 12, 16),
            Margin = new Thickness(0, 0, 4, 0),
            MinHeight = 56,
            Content = panel
        };
    }

    private static FrameworkElement CreateFavoritePlacePanel(
        IPlaceRegistrationDialogViewModel viewModel,
        Action refresh)
    {
        var panel = new StackPanel { Spacing = 4 };

        if (viewModel.FavoritePlaces.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "즐겨찾기한 장소가 없습니다.",
                Opacity = 0.7,
                FontSize = 12
            });
            return panel;
        }

        foreach (var place in viewModel.FavoritePlaces)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var selectButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = place.DisplayName, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = place.RegionSummary, Opacity = 0.7, FontSize = 12 }
                    }
                }
            };
            selectButton.Click += async (_, _) =>
            {
                await viewModel.SelectExistingPlaceAsync(place);
                refresh();
            };

            var favoriteButton = new Button
            {
                Content = "★",
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            favoriteButton.Click += async (_, _) =>
            {
                await viewModel.TogglePlaceFavoriteAsync(place);
                refresh();
            };

            Grid.SetColumn(selectButton, 0);
            Grid.SetColumn(favoriteButton, 1);
            row.Children.Add(selectButton);
            row.Children.Add(favoriteButton);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static TreeView CreateHierarchyTreeView(
        IPlaceRegistrationDialogViewModel viewModel,
        Action refresh)
    {
        var treeView = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            MaxHeight = 200
        };
        var placeNodes = new Dictionary<TreeViewNode, PlacePickerItemDto>();

        foreach (var country in viewModel.PlaceHierarchy)
        {
            var countryNode = new TreeViewNode { Content = country.Title };
            foreach (var region in country.Regions)
            {
                var regionNode = new TreeViewNode { Content = region.Title };
                foreach (var place in region.Places)
                {
                    var placeNode = new TreeViewNode { Content = place.DisplayName };
                    placeNodes[placeNode] = place;
                    regionNode.Children.Add(placeNode);
                }

                if (regionNode.Children.Count > 0)
                {
                    countryNode.Children.Add(regionNode);
                }
            }

            if (countryNode.Children.Count > 0)
            {
                treeView.RootNodes.Add(countryNode);
            }
        }

        treeView.ItemInvoked += async (_, args) =>
        {
            if (args.InvokedItem is TreeViewNode node
                && placeNodes.TryGetValue(node, out var place))
            {
                await viewModel.SelectExistingPlaceAsync(place);
                refresh();
            }
        };

        return treeView;
    }

    private static DataTemplate CreateExistingPlaceItemTemplate() =>
        (DataTemplate)XamlReader.Load(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Spacing="2" Padding="4">
                <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold" TextWrapping="Wrap"/>
                <TextBlock Text="{Binding RegionSummary}" Opacity="0.75" FontSize="12"/>
              </StackPanel>
            </DataTemplate>
            """);

    private static DataTemplate CreateNearbyItemTemplate() =>
        (DataTemplate)XamlReader.Load(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Spacing="2" Padding="4">
                <TextBlock Text="{Binding Name}" FontWeight="SemiBold" TextWrapping="Wrap"/>
                <TextBlock Text="{Binding DistanceText}" Opacity="0.75" FontSize="12"/>
                <TextBlock Text="{Binding Vicinity}" Opacity="0.65" TextWrapping="Wrap" FontSize="12"/>
              </StackPanel>
            </DataTemplate>
            """);

    private static DataTemplate CreateSuggestionItemTemplate() =>
        (DataTemplate)XamlReader.Load(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Spacing="2" Padding="4">
                <TextBlock Text="{Binding PrimaryText}" FontWeight="SemiBold" TextWrapping="Wrap"/>
                <TextBlock Text="{Binding SecondaryText}" Opacity="0.75" FontSize="12" TextWrapping="Wrap"/>
              </StackPanel>
            </DataTemplate>
            """);
}
