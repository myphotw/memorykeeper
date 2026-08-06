using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace MemoryKeeper.App.Views;

public sealed partial class HomePage : Page
{
    private readonly IResponsiveLayoutService _responsiveLayout;
    private bool _heroImagePrimed;

    public HomeViewModel ViewModel { get; }

    public HomePage(HomeViewModel viewModel, IResponsiveLayoutService responsiveLayout)
    {
        ViewModel = viewModel;
        _responsiveLayout = responsiveLayout;
        DataContext = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        _responsiveLayout.BreakpointChanged += OnBreakpointChanged;
        _responsiveLayout.LayoutChanged += OnLayoutChanged;
        SizeChanged += HomePage_OnSizeChanged;
        Loaded += (_, _) =>
        {
            ApplyDesktopLayout();
            ApplyCardShadows();
            UpdateHomeChrome();
            RebuildCountryDonut();
        };
    }

    private void OnBreakpointChanged(object? sender, LayoutBreakpointChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(ApplyDesktopLayout);

    private void OnLayoutChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(ApplyDesktopLayout);

    private void HomePage_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyDesktopLayout();

    private void ApplyDesktopLayout()
    {
        var wide = ActualWidth >= 960;
        HeroCard.Height = ActualHeight > 0 && ActualHeight < 860 ? 280 : 320;

        if (wide)
        {
            StatsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            StatsGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            StatsGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            StatsGrid.ColumnDefinitions[3].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(StatPhotosCard, 0);
            Grid.SetColumn(StatPhotosCard, 0);
            Grid.SetRow(StatGpsCard, 0);
            Grid.SetColumn(StatGpsCard, 1);
            Grid.SetRow(StatPlacesCard, 0);
            Grid.SetColumn(StatPlacesCard, 2);
            Grid.SetRow(StatCountriesCard, 0);
            Grid.SetColumn(StatCountriesCard, 3);

            BottomSplitGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            BottomSplitGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(RecentPhotosCard, 0);
            Grid.SetColumn(RecentPhotosCard, 0);
            Grid.SetRow(SummaryCard, 0);
            Grid.SetColumn(SummaryCard, 1);

            QuickActionsPanel.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            QuickActionsPanel.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            QuickActionsPanel.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            QuickActionsPanel.ColumnDefinitions[3].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(QuickImportCard, 0);
            Grid.SetColumn(QuickImportCard, 0);
            Grid.SetRow(QuickOrganizeCard, 0);
            Grid.SetColumn(QuickOrganizeCard, 1);
            Grid.SetRow(QuickMapCard, 0);
            Grid.SetColumn(QuickMapCard, 2);
            Grid.SetRow(QuickTravelCard, 0);
            Grid.SetColumn(QuickTravelCard, 3);

            HeroStatsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            StatsGrid.ColumnDefinitions[2].Width = new GridLength(0);
            StatsGrid.ColumnDefinitions[3].Width = new GridLength(0);
            Grid.SetRow(StatPhotosCard, 0);
            Grid.SetColumn(StatPhotosCard, 0);
            Grid.SetRow(StatGpsCard, 0);
            Grid.SetColumn(StatGpsCard, 1);
            Grid.SetRow(StatPlacesCard, 1);
            Grid.SetColumn(StatPlacesCard, 0);
            Grid.SetRow(StatCountriesCard, 1);
            Grid.SetColumn(StatCountriesCard, 1);

            BottomSplitGrid.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetRow(RecentPhotosCard, 0);
            Grid.SetColumn(RecentPhotosCard, 0);
            Grid.SetRow(SummaryCard, 1);
            Grid.SetColumn(SummaryCard, 0);

            QuickActionsPanel.ColumnDefinitions[2].Width = new GridLength(0);
            QuickActionsPanel.ColumnDefinitions[3].Width = new GridLength(0);
            Grid.SetRow(QuickImportCard, 0);
            Grid.SetColumn(QuickImportCard, 0);
            Grid.SetRow(QuickOrganizeCard, 0);
            Grid.SetColumn(QuickOrganizeCard, 1);
            Grid.SetRow(QuickMapCard, 1);
            Grid.SetColumn(QuickMapCard, 0);
            Grid.SetRow(QuickTravelCard, 1);
            Grid.SetColumn(QuickTravelCard, 1);

            HeroStatsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyCardShadows()
    {
        AttachShadow(HeroCard, 8);
        AttachShadow(StatPhotosCard, 4);
        AttachShadow(StatGpsCard, 4);
        AttachShadow(StatPlacesCard, 4);
        AttachShadow(StatCountriesCard, 4);
        AttachShadow(RecentVisitsCard, 5);
        AttachShadow(RecentPhotosCard, 5);
        AttachShadow(SummaryCard, 5);
        AttachShadow(QuickActionsCard, 5);
    }

    private static void AttachShadow(Border border, float depth)
    {
        border.Shadow = new ThemeShadow();
        border.Translation = new System.Numerics.Vector3(0, 0, depth);
    }

    private async void HomePage_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasHero || ViewModel.HasRecentImports || ViewModel.HasRecentVisits
            || ViewModel.Statistics.PhotoCount > 0)
        {
            ViewModel.ResumeHeroCarousel();
            UpdateHomeChrome();
            RebuildCountryDonut();
            return;
        }

        await ViewModel.LoadCommand.ExecuteAsync(null);
        UpdateHomeChrome();
        RebuildCountryDonut();
    }

    private void HomePage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _responsiveLayout.BreakpointChanged -= OnBreakpointChanged;
        _responsiveLayout.LayoutChanged -= OnLayoutChanged;
        ViewModel.Stop();
    }

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeViewModel.CurrentHero)
            or nameof(HomeViewModel.HasHero)
            or nameof(HomeViewModel.CurrentHeroImage))
        {
            UpdateHeroVisual(crossFade: _heroImagePrimed);
        }

        if (e.PropertyName is nameof(HomeViewModel.HasHero)
            or nameof(HomeViewModel.HasRecentImports)
            or nameof(HomeViewModel.HasRecentVisits)
            or nameof(HomeViewModel.IsBusy)
            or nameof(HomeViewModel.Statistics)
            or nameof(HomeViewModel.CountrySlices)
            or nameof(HomeViewModel.StatusMessage))
        {
            UpdateHomeChrome();
            RebuildCountryDonut();
        }
    }

    private void UpdateHomeChrome()
    {
        var stats = ViewModel.Statistics;
        var hasLibrary = ViewModel.HasHero
            || ViewModel.HasRecentImports
            || ViewModel.HasRecentVisits
            || stats.PhotoCount > 0;

        var showEmpty = !ViewModel.IsBusy && !hasLibrary;
        var showSkeleton = ViewModel.IsBusy && !hasLibrary;

        EmptyState.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        SkeletonRoot.Visibility = showSkeleton ? Visibility.Visible : Visibility.Collapsed;
        MainScroll.Visibility = showEmpty || showSkeleton ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RebuildCountryDonut()
    {
        CountryDonutCanvas.Children.Clear();
        var slices = ViewModel.CountrySlices;
        if (slices.Count == 0)
        {
            return;
        }

        const double size = 112;
        const double thickness = 22;
        var center = new Point(size / 2, size / 2);
        var outer = size / 2 - 2;
        var inner = outer - thickness;

        foreach (var slice in slices)
        {
            var path = new Microsoft.UI.Xaml.Shapes.Path
            {
                Fill = slice.Brush,
                Data = CreateDonutSlice(center, outer, inner, slice.StartAngle, slice.SweepAngle)
            };
            CountryDonutCanvas.Children.Add(path);
        }

        CountryDonutCanvas.Children.Add(new Ellipse
        {
            Width = inner * 2,
            Height = inner * 2,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 250, 250, 250))
        });
        Canvas.SetLeft(CountryDonutCanvas.Children[^1], center.X - inner);
        Canvas.SetTop(CountryDonutCanvas.Children[^1], center.Y - inner);
    }

    private static Geometry CreateDonutSlice(
        Point center,
        double outerRadius,
        double innerRadius,
        double startAngleDegrees,
        double sweepAngleDegrees)
    {
        if (sweepAngleDegrees >= 359.9)
        {
            // Full ring: outer circle minus hole via combined geometry isn't needed —
            // approximate with two half-slices.
            sweepAngleDegrees = 359.9;
        }

        var startOuter = PointOnCircle(center, outerRadius, startAngleDegrees);
        var endOuter = PointOnCircle(center, outerRadius, startAngleDegrees + sweepAngleDegrees);
        var startInner = PointOnCircle(center, innerRadius, startAngleDegrees);
        var endInner = PointOnCircle(center, innerRadius, startAngleDegrees + sweepAngleDegrees);
        var large = sweepAngleDegrees > 180;

        var figure = new PathFigure
        {
            StartPoint = startOuter,
            IsClosed = true,
            IsFilled = true
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = endOuter,
            Size = new Size(outerRadius, outerRadius),
            IsLargeArc = large,
            SweepDirection = SweepDirection.Clockwise
        });
        figure.Segments.Add(new LineSegment { Point = endInner });
        figure.Segments.Add(new ArcSegment
        {
            Point = startInner,
            Size = new Size(innerRadius, innerRadius),
            IsLargeArc = large,
            SweepDirection = SweepDirection.Counterclockwise
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = Math.PI * angleDegrees / 180.0;
        return new Point(
            center.X + (radius * Math.Cos(radians)),
            center.Y + (radius * Math.Sin(radians)));
    }

    private void UpdateHeroVisual(bool crossFade)
    {
        var image = ViewModel.CurrentHeroImage;
        if (image is null)
        {
            return;
        }

        if (!crossFade || HeroImageFront.Source is null)
        {
            HeroImageFront.Source = image;
            HeroImageFront.Opacity = 1;
            HeroImageBack.Opacity = 0;
            _heroImagePrimed = true;
            return;
        }

        if (ReferenceEquals(HeroImageFront.Source, image))
        {
            return;
        }

        HeroImageBack.Source = image;
        HeroImageBack.Opacity = 0;
        HeroImageFront.Opacity = 1;

        var storyboard = new Storyboard();
        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(400)
        };
        var fadeIn = new DoubleAnimation
        {
            To = 1,
            Duration = TimeSpan.FromMilliseconds(400)
        };
        Storyboard.SetTarget(fadeOut, HeroImageFront);
        Storyboard.SetTargetProperty(fadeOut, "Opacity");
        Storyboard.SetTarget(fadeIn, HeroImageBack);
        Storyboard.SetTargetProperty(fadeIn, "Opacity");
        storyboard.Children.Add(fadeOut);
        storyboard.Children.Add(fadeIn);
        storyboard.Completed += (_, _) =>
        {
            HeroImageFront.Source = image;
            HeroImageFront.Opacity = 1;
            HeroImageBack.Opacity = 0;
        };
        storyboard.Begin();
    }

    private void HeroCard_OnTapped(object sender, TappedRoutedEventArgs e) =>
        ViewModel.OpenHeroCommand.Execute(null);

    private void HeroNav_OnTapped(object sender, TappedRoutedEventArgs e) =>
        e.Handled = true;

    private void HeroPrevious_OnClick(object sender, RoutedEventArgs e) =>
        ViewModel.PreviousHeroCommand.Execute(null);

    private void HeroNext_OnClick(object sender, RoutedEventArgs e) =>
        ViewModel.NextHeroCommand.Execute(null);

    private void RecentVisitCard_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: HomeRecentVisitItem item })
        {
            ViewModel.OpenRecentVisitCommand.Execute(item);
        }
    }

    private void RecentImportThumb_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: HomePhotoItem item })
        {
            ViewModel.OpenRecentImportCommand.Execute(item);
        }
    }

    private void StatPhotos_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel.QuickGalleryCommand.Execute(null);
    }

    private void StatGps_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel.QuickVisitRecordCommand.Execute(null);
    }

    private void StatPlaces_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel.QuickVisitRecordCommand.Execute(null);
    }

    private void StatCountries_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel.OpenStatisticsCommand.Execute(null);
    }

    private void QuickImport_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel.QuickImportCommand.Execute(null);
    }

    private void QuickOrganize_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel.OpenPendingCommand.Execute(null);
    }

    private void QuickMap_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel.QuickVisitRecordCommand.Execute(null);
    }

    private void QuickTravel_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        ViewModel.OpenStatisticsCommand.Execute(null);
    }

    private void Card_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Shadow ??= new ThemeShadow();
            border.Translation = new System.Numerics.Vector3(0, 0, 12);
        }
    }

    private void Card_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            var depth = ReferenceEquals(border, HeroCard) ? 8f : 5f;
            border.Translation = new System.Numerics.Vector3(0, 0, depth);
        }
    }
}
