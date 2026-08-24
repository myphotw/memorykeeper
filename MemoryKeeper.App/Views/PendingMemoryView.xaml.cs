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
        ViewModel.OpenPlaceRegistrationRequested += OnOpenPlaceRegistrationRequested;
        ViewModel.OpenMemoRequested += OnOpenMemoRequested;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenPlaceRegistrationRequested -= OnOpenPlaceRegistrationRequested;
        ViewModel.OpenMemoRequested -= OnOpenMemoRequested;
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
            ViewModel.OpenPhotoDetailCommand.Execute(item);
        }
    }

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
        var item = ViewModel.SelectedGroupMedia.FirstOrDefault(media => media.IsIncluded)
            ?? ViewModel.SelectedGroupMedia.FirstOrDefault()
            ?? ViewModel.ReclassificationCandidates.FirstOrDefault(media => media.IsIncluded);
        if (item is null)
        {
            await UserFeedback.ShowInfoAsync(XamlRoot, "메모", "사진을 선택하세요.");
            return;
        }

        ViewModel.OpenPhotoDetailCommand.Execute(item);
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

        if (saved)
        {
            await UserFeedback.ShowInfoAsync(
                XamlRoot,
                "장소 등록",
                "위치정보가 등록되었습니다. 사진에 좌표가 반영되었고 미분류에서 제외됩니다.");
        }
        else if (!string.IsNullOrWhiteSpace(ViewModel.PlaceDialogStatus))
        {
            await UserFeedback.ShowInfoAsync(
                XamlRoot,
                "장소 등록",
                ViewModel.PlaceDialogStatus);
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
}
