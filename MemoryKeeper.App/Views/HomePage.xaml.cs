using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

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
        };
    }

    private void OnBreakpointChanged(object? sender, LayoutBreakpointChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(ApplyDesktopLayout);

    private void OnLayoutChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(ApplyDesktopLayout);

    private void HomePage_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyDesktopLayout();

    /// <summary>Desktop(V1) only — keep two columns when width allows.</summary>
    private void ApplyDesktopLayout()
    {
        var wide = ActualWidth >= 900;

        if (wide)
        {
            MidGrid.ColumnSpacing = 20;
            BottomGrid.ColumnSpacing = 20;
            MidGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            MidGrid.ColumnDefinitions[1].Width = new GridLength(1.35, GridUnitType.Star);
            BottomGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            BottomGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(RecentPhotosCard, 1);
            Grid.SetRow(RecentPhotosCard, 0);
            Grid.SetColumn(QuickActionsCard, 1);
            Grid.SetRow(QuickActionsCard, 0);
            MidGrid.Height = ActualHeight > 0 && ActualHeight < 900 ? 148 : 168;
        }
        else
        {
            MidGrid.ColumnSpacing = 0;
            BottomGrid.ColumnSpacing = 0;
            MidGrid.ColumnDefinitions[1].Width = new GridLength(0);
            BottomGrid.ColumnDefinitions[1].Width = new GridLength(0);
            Grid.SetColumn(RecentPhotosCard, 0);
            Grid.SetRow(RecentPhotosCard, 1);
            Grid.SetColumn(QuickActionsCard, 0);
            Grid.SetRow(QuickActionsCard, 1);
            MidGrid.Height = double.NaN; // Auto
        }

        HeroCard.Height = ActualHeight > 0 && ActualHeight < 900 ? 240 : 300;
    }

    private void ApplyCardShadows()
    {
        AttachShadow(HeroCard, 8);
        AttachShadow(ImportSummaryCard, 6);
        AttachShadow(RecentPhotosCard, 6);
        AttachShadow(RecentVisitsCard, 6);
        AttachShadow(QuickActionsCard, 6);
    }

    private static void AttachShadow(Border border, float depth)
    {
        border.Shadow = new ThemeShadow();
        border.Translation = new System.Numerics.Vector3(0, 0, depth);
    }

    private async void HomePage_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasHero || ViewModel.HasRecentImports || ViewModel.HasRecentVisits)
        {
            ViewModel.ResumeHeroCarousel();
            UpdateHomeChrome();
            return;
        }

        await ViewModel.LoadCommand.ExecuteAsync(null);
        UpdateHomeChrome();
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
            or nameof(HomeViewModel.HasPending)
            or nameof(HomeViewModel.PendingLatestImportedText)
            or nameof(HomeViewModel.IsBusy)
            or nameof(HomeViewModel.RecentImports)
            or nameof(HomeViewModel.Statistics))
        {
            UpdateHomeChrome();
        }
    }

    private void UpdateHomeChrome()
    {
        var hasLibrary = ViewModel.HasHero
            || ViewModel.HasRecentImports
            || ViewModel.HasRecentVisits
            || ViewModel.Statistics.PhotoCount > 0;

        var showEmpty = !ViewModel.IsBusy && !hasLibrary;
        EmptyState.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        MainContent.Visibility = showEmpty ? Visibility.Collapsed : Visibility.Visible;

        NoHeroHint.Visibility = !showEmpty && !ViewModel.HasHero
            ? Visibility.Visible
            : Visibility.Collapsed;

        ImportDateText.Text = !string.IsNullOrWhiteSpace(ViewModel.PendingLatestImportedText)
            ? ViewModel.PendingLatestImportedText
            : hasLibrary ? "가져오기 기록 있음" : "아직 가져오지 않음";

        var importCount = ViewModel.RecentImports.Count;
        ImportCountText.Text = importCount > 0
            ? $"최근 사진 {importCount}장"
            : ViewModel.Statistics.PhotoCount > 0
                ? $"라이브러리 {ViewModel.Statistics.PhotoCount}장"
                : "가져온 사진 없음";

        ImportStatusText.Text = ViewModel.HasPending
            ? ViewModel.PendingSummaryText
            : "정리할 사진이 없어요";
        ImportStatusText.Foreground = new SolidColorBrush(
            ViewModel.HasPending
                ? Windows.UI.Color.FromArgb(255, 212, 165, 116)
                : Windows.UI.Color.FromArgb(255, 107, 102, 96));
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

    private void HeroReplay_OnClick(object sender, RoutedEventArgs e) =>
        ViewModel.OpenHeroCommand.Execute(null);

    private void HeroPrevious_OnClick(object sender, RoutedEventArgs e) =>
        ViewModel.PreviousHeroCommand.Execute(null);

    private void HeroNext_OnClick(object sender, RoutedEventArgs e) =>
        ViewModel.NextHeroCommand.Execute(null);

    private void HeroIndicator_OnTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: HomeHeroIndicator indicator })
        {
            ViewModel.SelectHeroIndicatorCommand.Execute(indicator);
        }
    }

    private void RecentVisitList_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeRecentVisitItem item)
        {
            ViewModel.OpenRecentVisitCommand.Execute(item);
        }
    }

    private void RecentImportGrid_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomePhotoItem item)
        {
            ViewModel.OpenRecentImportCommand.Execute(item);
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

    private void Card_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Shadow ??= new ThemeShadow();
            border.Translation = new System.Numerics.Vector3(0, 0, 14);
        }
    }

    private void Card_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            var depth = ReferenceEquals(border, HeroCard) ? 8f : 6f;
            border.Translation = new System.Numerics.Vector3(0, 0, depth);
        }
    }
}
