using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace MemoryKeeper.App.Views;

public sealed partial class TravelRecordsPage : Page
{
    private readonly IResponsiveLayoutService _responsiveLayout;

    public TravelRecordsViewModel ViewModel { get; }

    /// <summary>Empty-state 「사진 가져오기」 — ViewModel 변경 없이 Page에서 연결.</summary>
    public event EventHandler? OpenImportRequested;

    public TravelRecordsPage(
        TravelRecordsViewModel viewModel,
        IResponsiveLayoutService responsiveLayout)
    {
        ViewModel = viewModel;
        _responsiveLayout = responsiveLayout;
        DataContext = viewModel;
        InitializeComponent();
        _responsiveLayout.BreakpointChanged += OnBreakpointChanged;
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
    }

    private async void TravelRecordsPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasYearChapters && !ViewModel.HasMostVisited && !ViewModel.HasRecent && !ViewModel.HasSeasons)
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }

        ApplyResponsiveLayout(_responsiveLayout.CurrentBreakpoint);
        RefreshDerivedUi();
    }

    private void TravelRecordsPage_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(_responsiveLayout.CurrentBreakpoint);

    private void OnBreakpointChanged(object? sender, LayoutBreakpointChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() => ApplyResponsiveLayout(e.Current));

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TravelRecordsViewModel.YearChapters)
            or nameof(TravelRecordsViewModel.HasYearChapters)
            or nameof(TravelRecordsViewModel.RecentPlaces)
            or nameof(TravelRecordsViewModel.HasRecent)
            or nameof(TravelRecordsViewModel.IsBusy))
        {
            DispatcherQueue.TryEnqueue(RefreshDerivedUi);
        }
    }

    private void ApplyResponsiveLayout(LayoutBreakpoint breakpoint)
    {
        ContentHost.HorizontalAlignment = HorizontalAlignment.Center;
        ApplyMemoryCardWidths();
    }

    private void MemoryCardsGrid_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyMemoryCardWidths();

    private void MemoryCardsGrid_OnContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is GridViewItem container)
        {
            container.Width = CalculateMemoryCardWidth();
        }
    }

    private void ApplyMemoryCardWidths()
    {
        var width = CalculateMemoryCardWidth();
        foreach (var item in ViewModel.MemoryCards)
        {
            if (MemoryCardsGrid.ContainerFromItem(item) is GridViewItem container)
            {
                container.Width = width;
            }
        }
    }

    private double CalculateMemoryCardWidth()
    {
        var available = Math.Max(280, MemoryCardsGrid.ActualWidth - 4);
        return available >= 900
            ? Math.Min(440, (available - 14) / 2)
            : Math.Min(440, available);
    }

    private void RefreshDerivedUi()
    {
        RefreshRecentInsight();
        RefreshStats();
    }

    private void RefreshRecentInsight()
    {
        if (ViewModel.HasRecent && ViewModel.RecentPlaces.Count > 0)
        {
            var recent = ViewModel.RecentPlaces[0];
            InsightRecentPlace.Text = recent.PlaceName;
            InsightRecentDate.Text = recent.LastVisitText;
            return;
        }

        var firstTrip = ViewModel.YearChapters
            .SelectMany(chapter => chapter.Trips)
            .FirstOrDefault();
        if (firstTrip is not null)
        {
            InsightRecentPlace.Text = firstTrip.TripName;
            InsightRecentDate.Text = firstTrip.PeriodText;
            return;
        }

        InsightRecentPlace.Text = "아직 기록이 없어요";
        InsightRecentDate.Text = string.Empty;
    }

    private void RefreshStats()
    {
        var trips = ViewModel.YearChapters.SelectMany(chapter => chapter.Trips).ToList();
        if (trips.Count == 0)
        {
            StatsHost.Visibility = Visibility.Collapsed;
            return;
        }

        StatsHost.Visibility = Visibility.Visible;
        StatTrips.Text = trips.Count.ToString();
        StatCountries.Text = ViewModel.VisitedForeignCountryCount.ToString("N0");
        StatPlaces.Text = ViewModel.DistinctPlaceCount.ToString("N0");
        StatPhotos.Text = ViewModel.UniquePhotoCount.ToString("N0");
    }

    private void InsightCard_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Shadow ??= new ThemeShadow();
            border.Translation = new System.Numerics.Vector3(0, 0, 12);
        }
    }

    private void InsightCard_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Translation = new System.Numerics.Vector3(0, 0, 4);
        }
    }

    private void FeaturedMemory_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel.FeaturedMemory is { } trip)
        {
            ViewModel.OpenTripCommand.Execute(trip);
        }
    }

    private void RecentInsight_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel.HasRecent)
        {
            ViewModel.OpenRecentDetailCommand.Execute(null);
            return;
        }

        var firstTrip = ViewModel.YearChapters.SelectMany(chapter => chapter.Trips).FirstOrDefault();
        if (firstTrip is not null)
        {
            ViewModel.OpenTripCommand.Execute(firstTrip);
        }
    }

    private void ImportPhotos_OnClick(object sender, RoutedEventArgs e) =>
        OpenImportRequested?.Invoke(this, EventArgs.Empty);

    private void MemoryCard_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TravelMemoryCardItem memory)
        {
            ViewModel.OpenMemoryCardCommand.Execute(memory);
        }
    }

    private void LongUnvisited_OnTapped(object sender, TappedRoutedEventArgs e) =>
        ViewModel.OpenLongUnvisitedDetailCommand.Execute(null);

    private void Farthest_OnTapped(object sender, TappedRoutedEventArgs e) =>
        ViewModel.OpenFarthestDetailCommand.Execute(null);
}
