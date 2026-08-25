using MemoryKeeper.App.Dialogs;
using MemoryKeeper.App.Maps.Google;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Windows.System;
using System.Globalization;

namespace MemoryKeeper.App.Views;

public sealed partial class PhotoDetailView : UserControl
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISettingRepository _settingRepository;
    private GoogleMapController? _mapController;
    private bool _isSplitting;
    private double _splitStartX;
    private double _splitStartWidth;
    private bool _handlersAttached;

    public bool IsPanelMode { get; private set; }

    public PhotoDetailViewModel ViewModel { get; }

    public PhotoDetailView(
        PhotoDetailViewModel viewModel,
        ILoggerFactory loggerFactory,
        ISettingRepository settingRepository)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        _loggerFactory = loggerFactory;
        _settingRepository = settingRepository;
        InitializeComponent();
    }

    public void ConfigurePanelMode()
    {
        IsPanelMode = true;
        TitleText.Text = "사진 상세";
        PhotoColumn.Width = new GridLength(0);
        SplitterColumn.Width = new GridLength(0);
        InfoColumn.Width = new GridLength(1, GridUnitType.Star);
        PhotoPane.Visibility = Visibility.Collapsed;
        Splitter.Visibility = Visibility.Collapsed;
        ZoomFitButton.Visibility = Visibility.Collapsed;
        ZoomInButton.Visibility = Visibility.Collapsed;
        ZoomOutButton.Visibility = Visibility.Collapsed;
        ActionStack.Orientation = Orientation.Vertical;
        ActionScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        ActionScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        KeyboardHint.Visibility = Visibility.Collapsed;
    }

    public async Task LoadMediaAsync(Guid mediaId)
    {
        await ViewModel.LoadMediaCommand.ExecuteAsync(mediaId);
        if (!IsPanelMode)
        {
            FitPhotoToPane();
        }
    }

    private async void PhotoDetailView_OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachHandlers();
        if (!IsPanelMode)
        {
            InfoColumn.Width = new GridLength(ViewModel.PanelWidth);
        }
        if (_mapController is null)
        {
            _mapController = new GoogleMapController(
                MapWebView,
                _loggerFactory.CreateLogger<GoogleMapController>());
            ViewModel.AttachMap(_mapController);

            try
            {
                var apiKey = await MapDisplayCredentialProvider.GetAsync(_settingRepository);
                await _mapController.InitializeAsync(apiKey);
            }
            catch
            {
                // Map is optional when API key is missing.
            }
        }

        if (ViewModel.MediaId == Guid.Empty)
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }

        if (!IsPanelMode)
        {
            InfoColumn.Width = new GridLength(ViewModel.PanelWidth);
            FitPhotoToPane();
        }
        Focus(FocusState.Programmatic);
    }

    private async void PhotoDetailView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!IsPanelMode)
        {
            ViewModel.PanelWidth = InfoColumn.Width.Value;
            await ViewModel.SavePanelWidthAsync();
        }

        DetachHandlers();
        ViewModel.DetachMap();
        if (_mapController is not null)
        {
            await _mapController.DisposeAsync();
            _mapController = null;
        }
    }

    private void AttachHandlers()
    {
        if (_handlersAttached)
        {
            return;
        }

        ViewModel.OpenPlaceRegistrationRequested += OnOpenPlaceRegistrationRequested;
        ViewModel.OpenTagManagerRequested += OnOpenTagManagerRequested;
        ViewModel.OpenMemoEditorRequested += OnOpenMemoEditorRequested;
        ViewModel.OpenRawLocationEditorRequested += OnOpenRawLocationEditorRequested;
        ViewModel.OpenMapPickRequested += OnOpenMapPickRequested;
        ViewModel.ToastRequested += OnToastRequested;
        _handlersAttached = true;
    }

    private void DetachHandlers()
    {
        if (!_handlersAttached)
        {
            return;
        }

        ViewModel.OpenPlaceRegistrationRequested -= OnOpenPlaceRegistrationRequested;
        ViewModel.OpenTagManagerRequested -= OnOpenTagManagerRequested;
        ViewModel.OpenMemoEditorRequested -= OnOpenMemoEditorRequested;
        ViewModel.OpenRawLocationEditorRequested -= OnOpenRawLocationEditorRequested;
        ViewModel.OpenMapPickRequested -= OnOpenMapPickRequested;
        ViewModel.ToastRequested -= OnToastRequested;
        _handlersAttached = false;
    }

    private async void PhotoDetailView_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left:
                await ViewModel.GoPreviousCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                await ViewModel.GoNextCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                await ShowPlaceRegistrationDialogAsync();
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                ViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void RelatedPhotos_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RelatedPhotoItem item)
        {
            _ = ViewModel.SelectRelatedCommand.ExecuteAsync(item);
        }
    }

    private void ZoomFit_OnClick(object sender, RoutedEventArgs e) => FitPhotoToPane();

    private void ZoomIn_OnClick(object sender, RoutedEventArgs e) =>
        PhotoScrollViewer.ChangeView(null, null, Math.Min(8.0f, PhotoScrollViewer.ZoomFactor + 0.25f));

    private void ZoomOut_OnClick(object sender, RoutedEventArgs e) =>
        PhotoScrollViewer.ChangeView(null, null, Math.Max(0.25f, PhotoScrollViewer.ZoomFactor - 0.25f));

    private void PhotoScrollViewer_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var fit = GetFitZoomFactor();
        var next = Math.Abs(PhotoScrollViewer.ZoomFactor - fit) < 0.05f
            ? Math.Min(8.0f, fit * 2.0f)
            : fit;
        PhotoScrollViewer.ChangeView(null, null, next);
    }

    private void PhotoPane_OnSizeChanged(object sender, SizeChangedEventArgs e) => FitPhotoToPane();

    private void FitPhotoToPane()
    {
        var width = Math.Max(1, PhotoPane.ActualWidth - 8);
        var height = Math.Max(1, PhotoPane.ActualHeight - 8);
        PhotoImageControl.MaxWidth = width;
        PhotoImageControl.MaxHeight = height;
        PhotoImageControl.Width = width;
        PhotoImageControl.Height = height;
        PhotoScrollViewer.ChangeView(null, null, 1.0f, disableAnimation: true);
    }

    private float GetFitZoomFactor() => 1.0f;

    private void Splitter_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        _isSplitting = true;
        _splitStartX = e.GetCurrentPoint(this).Position.X;
        _splitStartWidth = InfoColumn.Width.Value;
        element.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Splitter_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSplitting)
        {
            return;
        }

        var delta = _splitStartX - e.GetCurrentPoint(this).Position.X;
        var width = Math.Clamp(_splitStartWidth + delta, 240, 720);
        InfoColumn.Width = new GridLength(width);
        ViewModel.PanelWidth = width;
        FitPhotoToPane();
        e.Handled = true;
    }

    private async void Splitter_OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSplitting)
        {
            return;
        }

        _isSplitting = false;
        if (sender is FrameworkElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
        }

        await ViewModel.SavePanelWidthAsync();
    }

    private async void ShowExif_OnClick(object sender, RoutedEventArgs e)
    {
        var text = await ViewModel.BuildExifDebugTextAsync();
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            MinHeight = 360,
            MinWidth = 520
        };

        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "EXIF 요약",
            CloseButtonText = "닫기",
            DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer { MaxHeight = 480, Content = box }
        }.ShowAsync();
    }

    private async void RemoveTag_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TagChipItem tag })
        {
            await ViewModel.RemoveTagCommand.ExecuteAsync(tag);
        }
    }

    private async void OnOpenPlaceRegistrationRequested(object? sender, EventArgs e) =>
        await ShowPlaceRegistrationDialogAsync();

    private async void OnOpenTagManagerRequested(object? sender, EventArgs e) =>
        await ShowTagDialogAsync();

    private async void OnOpenMemoEditorRequested(object? sender, EventArgs e) =>
        await ShowMemoDialogAsync();

    private async void OnOpenRawLocationEditorRequested(object? sender, EventArgs e) =>
        await ShowRawLocationDialogAsync();

    private async void OnOpenMapPickRequested(object? sender, EventArgs e) =>
        await ShowMapPickDialogAsync();

    private async void OnToastRequested(object? sender, string message) =>
        await UserFeedback.ShowInfoAsync(XamlRoot, "알림", message);

    private async Task ShowMemoDialogAsync()
    {
        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 160,
            MinWidth = 360,
            Text = ViewModel.MemoDraft
        };
        box.TextChanged += (_, _) => ViewModel.MemoDraft = box.Text;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "메모 수정",
            PrimaryButtonText = "저장",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            Content = box
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.SaveMemoCommand.ExecuteAsync(null);
        }
    }

    private async Task ShowRawLocationDialogAsync()
    {
        var latitude = new TextBox { Header = "GPS 위도", Text = ViewModel.Latitude?.ToString(CultureInfo.InvariantCulture) ?? string.Empty };
        var longitude = new TextBox { Header = "GPS 경도", Text = ViewModel.Longitude?.ToString(CultureInfo.InvariantCulture) ?? string.Empty };
        var country = new TextBox { Header = "국가", Text = CleanDisplayValue(ViewModel.Country) };
        var province = new TextBox { Header = "시/도", Text = CleanDisplayValue(ViewModel.Province) };
        var city = new TextBox { Header = "시/군/구", Text = CleanDisplayValue(ViewModel.City) };
        var district = new TextBox { Header = "세부 지역", Text = CleanDisplayValue(ViewModel.District) };
        var placeName = new TextBox { Header = "원본 주소/장소명", Text = ViewModel.Address, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "원본 GPS·주소 수정",
            PrimaryButtonText = "저장",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                MaxHeight = 520,
                Content = new StackPanel
                {
                    Spacing = 8,
                    MinWidth = 380,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "사진의 원본 위치 정보입니다. 대표 추억 장소 지정과는 별도로 저장됩니다.",
                            TextWrapping = TextWrapping.Wrap,
                            Opacity = 0.8,
                        },
                        latitude, longitude, country, province, city, district, placeName,
                    },
                },
            },
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (!TryNullableDouble(latitude.Text, out var lat)
            || !TryNullableDouble(longitude.Text, out var lon))
        {
            await UserFeedback.ShowInfoAsync(XamlRoot, "위치 정보", "GPS 좌표를 숫자로 입력하세요.");
            return;
        }

        await ViewModel.SaveRawLocationAsync(
            lat, lon, country.Text, province.Text, city.Text, district.Text, placeName.Text);
    }

    private async void DeleteFromMemoryKeeper_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "사진 삭제",
            Content = "이 사진을 MemoryKeeper에서 삭제할까요?",
            PrimaryButtonText = "삭제",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteFromLibraryCommand.ExecuteAsync(null);
        }
    }

    private static bool TryNullableDouble(string? value, out double? result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            result = parsed;
            return true;
        }

        result = null;
        return false;
    }

    private static string CleanDisplayValue(string value) => value == "-" ? string.Empty : value;

    private async Task ShowTagDialogAsync()
    {
        await ViewModel.LoadTagPickerAsync();

        var listHost = new StackPanel { Spacing = 8 };
        void Rebuild()
        {
            listHost.Children.Clear();
            AddSection(listHost, "고정 태그", ViewModel.TagPickerPinnedItems);
            AddSection(listHost, "최근 사용", ViewModel.TagPickerRecentItems);
            AddSection(listHost, "현재 공통 태그", ViewModel.TagPickerCommonItems);
            AddSection(listHost, "추가할 태그 / 인기 태그", ViewModel.TagPickerCandidateItems);
        }

        Rebuild();

        var searchBox = new TextBox { PlaceholderText = "태그 검색", Text = ViewModel.TagSearchKeyword };
        searchBox.TextChanged += async (_, _) =>
        {
            ViewModel.TagSearchKeyword = searchBox.Text;
            await ViewModel.SearchTagPickerCommand.ExecuteAsync(null);
            Rebuild();
        };

        var createBox = new TextBox { PlaceholderText = "새 태그 이름 (가족, 여행, 바다…)", Text = ViewModel.NewTagName };
        createBox.TextChanged += (_, _) => ViewModel.NewTagName = createBox.Text;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "태그 관리",
            PrimaryButtonText = "추가",
            CloseButtonText = "닫기",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 12,
                MinWidth = 320,
                Children =
                {
                    searchBox,
                    new ScrollViewer { MaxHeight = 320, Content = listHost },
                    new TextBlock { Text = "새 태그 만들기", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    createBox
                }
            }
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.AssignTagsFromPickerAsync();
        }
    }

    private async Task ShowPlaceRegistrationDialogAsync()
    {
        ViewModel.HostXamlRoot = XamlRoot;
        var saved = await PlaceRegistrationDialog.ShowAsync(
            XamlRoot,
            ViewModel,
            new PlaceRegistrationDialog.Options
            {
                Title = "위치정보 추가/수정",
                PrimaryButtonText = "적용",
                SupportsMapPick = true,
                MapPickHandler = ShowMapPickInPlaceDialogAsync
            });

        if (!saved && !string.IsNullOrWhiteSpace(ViewModel.PlaceDialogStatus))
        {
            await UserFeedback.ShowInfoAsync(XamlRoot, "위치정보", ViewModel.PlaceDialogStatus);
        }
    }

    private async Task ShowMapPickInPlaceDialogAsync(ContentDialog host)
    {
        await MapPickSession.RunInDialogAsync(
            host,
            _loggerFactory,
            _settingRepository,
            ViewModel.MapPickLatitude,
            ViewModel.MapPickLongitude,
            ViewModel.MapPickRadiusMeters,
            async (lat, lng, radius) =>
            {
                await ViewModel.ApplyMapPickAsync(lat, lng, radius);
                return ViewModel.PlaceDialogStatus;
            },
            ViewModel.DiscardMapPickSelection,
            new MapPickSession.SearchHooks
            {
                SearchAsync = async query =>
                {
                    ViewModel.PlaceSearchText = query;
                    await ViewModel.SearchPlaceSuggestionsAsync();
                    return ViewModel.PlaceSearchResults;
                },
                ResolveCoordinatesAsync = ViewModel.ResolveSuggestionCoordinatesAsync
            });
    }

    private async Task ShowMapPickDialogAsync()
    {
        var webView = new WebView2 { Width = 520, Height = 360 };
        var radiusBox = new NumberBox
        {
            Header = "반경 (m)",
            Value = ViewModel.MapPickRadiusMeters,
            Minimum = 20,
            Maximum = 2000,
            SmallChange = 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        var status = new TextBlock
        {
            Text = "핀을 드래그하거나 지도를 클릭하세요.",
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap
        };

        var mapController = new GoogleMapController(
            webView,
            _loggerFactory.CreateLogger<GoogleMapController>());

        var currentLat = ViewModel.MapPickLatitude;
        var currentLng = ViewModel.MapPickLongitude;

        async Task ApplyPointAsync(double lat, double lng)
        {
            currentLat = lat;
            currentLng = lng;
            await ViewModel.ApplyMapPickAsync(lat, lng, radiusBox.Value);
            status.Text = ViewModel.PlaceDialogStatus;
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
            await ViewModel.ApplyMapPickAsync(currentLat, currentLng, args.NewValue);
            status.Text = ViewModel.PlaceDialogStatus;
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "지도에서 선택",
            PrimaryButtonText = "핀 확정",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { webView, radiusBox, status }
            }
        };

        try
        {
            var apiKey = await MapDisplayCredentialProvider.GetAsync(_settingRepository);
            await mapController.InitializeAsync(apiKey);
            await mapController.EnableMapClickAsync(true);
            await mapController.SetEditablePinAsync(
                ViewModel.MapPickLatitude,
                ViewModel.MapPickLongitude,
                radiusBox.Value);
            await ApplyPointAsync(ViewModel.MapPickLatitude, ViewModel.MapPickLongitude);
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
        }

        var result = await dialog.ShowAsync();
        mapController.EditableMarkerDragEnded -= dragHandler;
        mapController.MapClicked -= clickHandler;
        await mapController.DisposeAsync();

        if (result != ContentDialogResult.Primary)
        {
            ViewModel.DiscardMapPickSelection();
        }
    }

    private static void AddSection(StackPanel host, string title, IEnumerable<TagChipItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        host.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        foreach (var item in list)
        {
            var check = new CheckBox
            {
                Content = item.DisplayText,
                IsChecked = item.IsSelected,
                IsEnabled = !item.IsAssigned
            };
            check.Checked += (_, _) => item.IsSelected = true;
            check.Unchecked += (_, _) => item.IsSelected = false;
            host.Children.Add(check);
        }
    }
}
