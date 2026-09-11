using MemoryKeeper.App.Dialogs;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace MemoryKeeper.App.Views;

public sealed partial class PendingMemoryView : UserControl
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISettingRepository _settingRepository;

    public PendingMemoryViewModel ViewModel { get; }

    public PendingMemoryView(
        PendingMemoryViewModel viewModel,
        ILoggerFactory loggerFactory,
        ISettingRepository settingRepository)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        _loggerFactory = loggerFactory;
        _settingRepository = settingRepository;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.HostXamlRoot = XamlRoot;
        ViewModel.OpenPlaceRegistrationRequested += OnOpenPlaceRegistrationRequested;
        ViewModel.OpenMemoRequested += OnOpenMemoRequested;
        ViewModel.OpenCaptureDateEditorRequested += OnOpenCaptureDateEditorRequested;
        ViewModel.ClearCaptureDateRequested += OnClearCaptureDateRequested;
        ViewModel.CaptureDateFeedbackRequested += OnCaptureDateFeedbackRequested;
        ViewModel.RadiusExpansionPreviewHandler = ShowRadiusExpansionPreviewAsync;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenPlaceRegistrationRequested -= OnOpenPlaceRegistrationRequested;
        ViewModel.OpenMemoRequested -= OnOpenMemoRequested;
        ViewModel.OpenCaptureDateEditorRequested -= OnOpenCaptureDateEditorRequested;
        ViewModel.ClearCaptureDateRequested -= OnClearCaptureDateRequested;
        ViewModel.CaptureDateFeedbackRequested -= OnCaptureDateFeedbackRequested;
        ViewModel.RadiusExpansionPreviewHandler = null;
    }

    private void GpsSection_OnTapped(object sender, TappedRoutedEventArgs e) =>
        ViewModel.SelectGpsSectionCommand.Execute(null);

    private void GpsCandidates_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedItem: PendingMemoryMediaItem })
        {
            ViewModel.SelectGpsSectionCommand.Execute(null);
        }
    }

    private void PendingMedia_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PendingMemoryMediaItem item })
        {
            ViewModel.ActivateMediaCommand.Execute(item);
        }
    }

    private void IncludeCheckBox_OnTapped(object sender, TappedRoutedEventArgs e) =>
        e.Handled = true;

    private void PendingMediaDetail_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PendingMemoryMediaItem item })
        {
            ViewModel.OpenPhotoDetailCommand.Execute(item);
        }
    }

    private async void OnOpenPlaceRegistrationRequested(object? sender, EventArgs e)
    {
        await ShowPlaceRegistrationDialogAsync();
    }

    private async void OnOpenMemoRequested(object? sender, EventArgs e)
    {
        var item = ViewModel.ActiveMediaItems.FirstOrDefault(media => media.IsIncluded)
            ?? ViewModel.ActiveMediaItems.FirstOrDefault();
        if (item is null)
        {
            await UserFeedback.ShowInfoAsync(XamlRoot, "메모", "사진을 선택하세요.");
            return;
        }

        ViewModel.OpenPhotoDetailCommand.Execute(item);
    }

    private async void OnOpenCaptureDateEditorRequested(object? sender, EventArgs e)
    {
        var representative = ViewModel.GetRepresentativeSelectedMedia();
        if (representative is null)
        {
            return;
        }

        var selectedDate = await CaptureDateDialog.ShowChangeAsync(
            XamlRoot,
            ViewModel.IncludedCount,
            representative.ThumbnailImage,
            ViewModel.SelectedDateStatusText,
            ResolveCaptureDate(representative));
        if (selectedDate is DateOnly date)
        {
            await ViewModel.ChangeCaptureDateAsync(date);
        }
    }

    private async void OnClearCaptureDateRequested(object? sender, EventArgs e)
    {
        var count = ViewModel.ActiveMediaItems.Count(item => item.IsIncluded && item.HasUserCaptureOverride);
        if (count == 0 || !await CaptureDateDialog.ConfirmClearAsync(XamlRoot, count))
        {
            return;
        }

        await ViewModel.ChangeCaptureDateAsync(userCaptureDate: null, clearOnlyOverrides: true);
    }

    private async void OnCaptureDateFeedbackRequested(object? sender, string message) =>
        await UserFeedback.ShowInfoAsync(XamlRoot, "촬영일 변경", message);

    private static DateOnly? ResolveCaptureDate(PendingMemoryMediaItem item) =>
        DateOnly.TryParseExact(
            item.Media.EffectiveCaptureDate,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var date)
            ? date
            : item.Media.CapturedAt is DateTimeOffset capturedAt
                ? DateOnly.FromDateTime(capturedAt.ToLocalTime().Date)
                : null;

    private async Task ShowPlaceRegistrationDialogAsync()
    {
        ViewModel.HostXamlRoot = XamlRoot;
        await PlaceRegistrationDialog.ShowAsync(
            XamlRoot,
            ViewModel,
            new PlaceRegistrationDialog.Options
            {
                Title = "위치정보 추가/수정",
                PrimaryButtonText = "적용",
                SupportsMapPick = true,
                MapPickHandler = ShowMapPickInPlaceDialogAsync
            });

        if (!string.IsNullOrWhiteSpace(ViewModel.PlaceDialogStatus))
        {
            await UserFeedback.ShowInfoAsync(
                XamlRoot,
                "장소 등록",
                ViewModel.PlaceDialogStatus);
        }
    }

    private Task<bool> ShowRadiusExpansionPreviewAsync(
        string placeName,
        MemoryKeeper.Application.PlaceRadiusExpansionPlan plan) =>
        PlaceRadiusExpansionDialog.ShowAsync(
            XamlRoot,
            _loggerFactory,
            _settingRepository,
            placeName,
            plan);

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
}
