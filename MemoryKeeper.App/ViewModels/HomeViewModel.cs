using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private static readonly TimeSpan HeroInterval = TimeSpan.FromSeconds(4);

    private readonly IGalleryApiRepository _galleryApiRepository;
    private readonly IThumbnailService _thumbnailService;
    private readonly IPlaceFocusState _placeFocusState;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly ITravelRecordsNavigationState _travelRecordsNavigationState;
    private readonly ILogger<HomeViewModel> _logger;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _heroTimer;
    private CancellationTokenSource? _thumbnailCts;
    private bool _heroPaused;
    private int _heroIndex;

    [ObservableProperty]
    private ObservableCollection<HomeHeroItem> heroMemories = [];

    [ObservableProperty]
    private ObservableCollection<HomeHeroIndicator> heroIndicators = [];

    [ObservableProperty]
    private HomeHeroItem? currentHero;

    [ObservableProperty]
    private string currentHeroTitle = string.Empty;

    [ObservableProperty]
    private string currentHeroSubtitle = string.Empty;

    [ObservableProperty]
    private string currentHeroKindLabel = string.Empty;

    [ObservableProperty]
    private string currentHeroDescription = string.Empty;

    [ObservableProperty]
    private string currentHeroPhotoCountText = string.Empty;

    [ObservableProperty]
    private BitmapImage? currentHeroImage;

    [ObservableProperty]
    private bool hasHeroCarousel;

    [ObservableProperty]
    private ObservableCollection<HomeTodayItem> todayMemories = [];

    [ObservableProperty]
    private ObservableCollection<HomeRecentVisitItem> recentVisits = [];

    [ObservableProperty]
    private ObservableCollection<HomePhotoItem> favorites = [];

    [ObservableProperty]
    private ObservableCollection<HomePhotoItem> recentImports = [];

    [ObservableProperty]
    private ObservableCollection<string> recentQueries = [];

    [ObservableProperty]
    private PendingSummaryDto pendingSummary = new();

    [ObservableProperty]
    private DashboardStatisticsDto statistics = new();

    [ObservableProperty]
    private string statusMessage = "추억을 불러오는 중…";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasHero;

    [ObservableProperty]
    private bool hasTodayMemories;

    [ObservableProperty]
    private bool hasRecentVisits;

    [ObservableProperty]
    private bool hasFavorites;

    [ObservableProperty]
    private bool hasRecentImports;

    [ObservableProperty]
    private bool hasPending;

    [ObservableProperty]
    private bool hasRecentQueries;

    [ObservableProperty]
    private string pendingSummaryText = string.Empty;

    [ObservableProperty]
    private string pendingLatestImportedText = string.Empty;

    [ObservableProperty]
    private BitmapImage? pendingThumbnailImage;

    [ObservableProperty]
    private string statisticsSummaryText = string.Empty;

    public event EventHandler? OpenVisitRecordRequested;
    public event EventHandler? OpenGalleryRequested;
    public event EventHandler? OpenPendingRequested;
    public event EventHandler? OpenImportRequested;
    public event EventHandler? OpenTagRequested;
    public event EventHandler? OpenPlaceRequested;
    public event EventHandler? OpenStorageRequested;
    public event EventHandler? OpenStatisticsRequested;
    public event EventHandler? OpenSettingsRequested;

    public HomeViewModel(
        IGalleryApiRepository galleryApiRepository,
        IThumbnailService thumbnailService,
        IPlaceFocusState placeFocusState,
        IPhotoNavigationState photoNavigationState,
        ITravelRecordsNavigationState travelRecordsNavigationState,
        ILogger<HomeViewModel> logger)
    {
        _galleryApiRepository = galleryApiRepository;
        _thumbnailService = thumbnailService;
        _placeFocusState = placeFocusState;
        _photoNavigationState = photoNavigationState;
        _travelRecordsNavigationState = travelRecordsNavigationState;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _heroTimer = _dispatcherQueue.CreateTimer();
        _heroTimer.Interval = HeroInterval;
        _heroTimer.IsRepeating = true;
        _heroTimer.Tick += (_, _) =>
        {
            if (!_heroPaused && HeroMemories.Count > 1)
            {
                NextHero();
            }
        };
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        StopHeroTimer();
        CancelThumbnailLoading();
        _thumbnailCts = new CancellationTokenSource();
        var token = _thumbnailCts.Token;

        // Keep IsBusy only for dashboard data so a slow/hung thumbnail decode
        // cannot leave the home ProgressRing spinning forever.
        await RunBusyAsync(async () =>
        {
            ClearLocalDashboardSections();
            try
            {
                Statistics = await GalleryBackendBridge.GetStatisticsAsync(_galleryApiRepository, token);
                StatusMessage = "Backend 통계를 불러왔습니다.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backend statistics failed.");
                Statistics = new DashboardStatisticsDto();
                StatusMessage = $"통계를 불러오지 못했습니다. {ex.Message}";
            }

            UpdateStatisticsSummary();
        });

        if (token.IsCancellationRequested)
        {
            return;
        }

        await PreloadHeroThumbnailsAsync(token);
        _ = LoadSectionThumbnailsAsync(token);
        StartHeroTimer();
    }

    [RelayCommand]
    private void OpenHero(HomeHeroItem? item)
    {
        var target = item ?? CurrentHero;
        if (target is null)
        {
            return;
        }

        // 발견(Home) → 회상(여행기록). 사진·지도는 Timeline에서 이어간다.
        _travelRecordsNavigationState.RequestMemoryFocus(
            target.PlaceId,
            target.Dto.Year > 0 ? target.Dto.Year : null,
            target.Dto.PlaceName,
            target.RepresentativeMediaId);
        OpenStatisticsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenTodayPhoto(HomeTodayItem? item)
    {
        if (item is null)
        {
            return;
        }

        _photoNavigationState.RequestOpen(item.MediaId);
    }

    [RelayCommand]
    private void OpenRecentVisit(HomeRecentVisitItem? item)
    {
        if (item is null)
        {
            return;
        }

        _placeFocusState.FocusPlaceId = item.PlaceId;
        _placeFocusState.PendingSearchText = null;
        OpenVisitRecordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenFavorite(HomePhotoItem? item)
    {
        if (item is null)
        {
            return;
        }

        _photoNavigationState.RequestOpen(item.MediaId);
    }

    [RelayCommand]
    private void OpenRecentImport(HomePhotoItem? item)
    {
        if (item is null)
        {
            return;
        }

        var playlist = RecentImports.Select(x => x.MediaId).ToList();
        _photoNavigationState.RequestOpenViewer(item.MediaId, playlist, "home");
    }

    [RelayCommand]
    private void OpenPending()
    {
        if (PendingSummary.RepresentativeMediaId is Guid mediaId)
        {
            _photoNavigationState.RequestOpen(mediaId);
            return;
        }

        OpenPendingRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenRecentQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        _placeFocusState.PendingSearchText = query;
        _placeFocusState.FocusPlaceId = null;
        OpenVisitRecordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenStatistics()
    {
        OpenStatisticsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenRecentActivityImports()
    {
        if (RecentImports.Count > 0)
        {
            OpenRecentImport(RecentImports[0]);
            return;
        }

        OpenGalleryRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenRecentActivityVisits()
    {
        if (RecentVisits.Count > 0)
        {
            OpenRecentVisit(RecentVisits[0]);
            return;
        }

        _placeFocusState.PendingSearchText = null;
        OpenVisitRecordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void QuickImport() => OpenImportRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void QuickGallery() => OpenGalleryRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void QuickVisitRecord()
    {
        _placeFocusState.PendingSearchText = null;
        OpenVisitRecordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void QuickTravel() => OpenStatisticsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void QuickSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void QuickTag() => OpenTagRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void QuickPlace() => OpenPlaceRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void QuickStorage() => OpenStorageRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void NextHero()
    {
        if (HeroMemories.Count == 0)
        {
            return;
        }

        SetHeroIndex((_heroIndex + 1) % HeroMemories.Count);
    }

    [RelayCommand]
    private void PreviousHero()
    {
        if (HeroMemories.Count == 0)
        {
            return;
        }

        SetHeroIndex((_heroIndex - 1 + HeroMemories.Count) % HeroMemories.Count);
    }

    [RelayCommand]
    private void SelectHeroIndicator(HomeHeroIndicator? indicator)
    {
        if (indicator is null)
        {
            return;
        }

        SetHeroIndex(indicator.Index);
    }

    public void PauseHeroCarousel()
    {
        _heroPaused = true;
    }

    public void ResumeHeroCarousel()
    {
        _heroPaused = false;
    }

    public void Stop()
    {
        StopHeroTimer();
        CancelThumbnailLoading();
    }

    private void ClearLocalDashboardSections()
    {
        HeroMemories = [];
        HeroIndicators = [];
        TodayMemories = [];
        RecentVisits = [];
        Favorites = [];
        RecentImports = [];
        RecentQueries = [];
        PendingSummary = new PendingSummaryDto();
        PendingSummaryText = string.Empty;
        PendingLatestImportedText = string.Empty;
        PendingThumbnailImage = null;
        StatisticsSummaryText = "시간을 따라 여행을 다시 만나 보세요.";
        HasHero = false;
        HasHeroCarousel = false;
        HasTodayMemories = false;
        HasRecentVisits = false;
        HasFavorites = false;
        HasRecentImports = false;
        HasPending = false;
        HasRecentQueries = false;
        _heroIndex = 0;
        SetHeroIndex(0);
    }

    private void UpdateStatisticsSummary()
    {
        StatisticsSummaryText =
            $"사진 {Statistics.PhotoCount} · 장소 {Statistics.PlaceCount} · 즐겨찾기 {Statistics.FavoriteCount}";
    }

    private void ApplyDashboard(HomeDashboardDto dashboard)
    {
        HeroMemories = new ObservableCollection<HomeHeroItem>(
            dashboard.HeroMemories.Select(dto => new HomeHeroItem(dto)));
        HeroIndicators = new ObservableCollection<HomeHeroIndicator>(
            HeroMemories.Select((_, index) => new HomeHeroIndicator(index)));
        TodayMemories = new ObservableCollection<HomeTodayItem>(
            dashboard.TodayMemories.Select(dto => new HomeTodayItem(dto)));
        RecentVisits = new ObservableCollection<HomeRecentVisitItem>(
            dashboard.RecentVisits.Take(3).Select(dto => new HomeRecentVisitItem(dto)));
        Favorites = new ObservableCollection<HomePhotoItem>(
            dashboard.Favorites.Select(dto => new HomePhotoItem(dto)));
        RecentImports = new ObservableCollection<HomePhotoItem>(
            dashboard.RecentImports.Take(6).Select(dto => new HomePhotoItem(dto)));
        RecentQueries = new ObservableCollection<string>(dashboard.RecentQueries);
        PendingSummary = dashboard.PendingSummary;
        Statistics = dashboard.Statistics;
        PendingSummaryText =
            $"아직 장소를 정하지 못한 사진이 있어요";
        PendingLatestImportedText = PendingSummary.LatestImportedAt.HasValue
            ? $"최근 담은 날 {PendingSummary.LatestImportedAt.Value.ToLocalTime():yyyy.MM.dd}"
            : string.Empty;
        PendingThumbnailImage = null;
        StatisticsSummaryText = "시간을 따라 여행을 다시 만나 보세요.";

        HasHero = HeroMemories.Count > 0;
        HasHeroCarousel = HeroMemories.Count > 1;
        HasTodayMemories = TodayMemories.Count > 0;
        HasRecentVisits = RecentVisits.Count > 0;
        HasFavorites = Favorites.Count > 0;
        HasRecentImports = RecentImports.Count > 0;
        HasPending = PendingSummary.HasItems;
        HasRecentQueries = RecentQueries.Count > 0;

        _heroIndex = 0;
        SetHeroIndex(0);
    }

    private void SetHeroIndex(int index)
    {
        if (CurrentHero is not null)
        {
            CurrentHero.PropertyChanged -= HeroItem_OnPropertyChanged;
        }

        if (HeroMemories.Count == 0)
        {
            CurrentHero = null;
            CurrentHeroTitle = string.Empty;
            CurrentHeroSubtitle = string.Empty;
            CurrentHeroKindLabel = string.Empty;
            CurrentHeroDescription = string.Empty;
            CurrentHeroPhotoCountText = string.Empty;
            CurrentHeroImage = null;
            return;
        }

        _heroIndex = Math.Clamp(index, 0, HeroMemories.Count - 1);
        CurrentHero = HeroMemories[_heroIndex];
        CurrentHeroTitle = CurrentHero.Title;
        CurrentHeroSubtitle = CurrentHero.Subtitle;
        CurrentHeroKindLabel = CurrentHero.KindLabel;
        CurrentHeroDescription = CurrentHero.Description;
        CurrentHeroPhotoCountText = CurrentHero.PhotoCountText;
        CurrentHeroImage = CurrentHero.ThumbnailImage;
        CurrentHero.PropertyChanged += HeroItem_OnPropertyChanged;
        UpdateHeroIndicators();
    }

    private void HeroItem_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is HomeHeroItem hero
            && ReferenceEquals(hero, CurrentHero)
            && e.PropertyName == nameof(HomeHeroItem.ThumbnailImage))
        {
            CurrentHeroImage = hero.ThumbnailImage;
        }
    }

    private void UpdateHeroIndicators()
    {
        for (var i = 0; i < HeroIndicators.Count; i++)
        {
            HeroIndicators[i].IsSelected = i == _heroIndex;
        }

        for (var i = 0; i < HeroMemories.Count; i++)
        {
            HeroMemories[i].IsSelected = i == _heroIndex;
        }
    }

    private async Task PreloadHeroThumbnailsAsync(CancellationToken token)
    {
        foreach (var hero in HeroMemories.ToList())
        {
            token.ThrowIfCancellationRequested();
            await LoadThumbnailAsync(
                hero.RepresentativeMediaId,
                hero.AbsoluteLibraryPath,
                image => hero.ThumbnailImage = image,
                loading => hero.IsThumbnailLoading = loading,
                token);
        }

        // Background preload already done sequentially; keep CurrentHero image ready.
        if (CurrentHero?.ThumbnailImage is null && CurrentHero is not null)
        {
            await LoadThumbnailAsync(
                CurrentHero.RepresentativeMediaId,
                CurrentHero.AbsoluteLibraryPath,
                image => CurrentHero.ThumbnailImage = image,
                loading => CurrentHero.IsThumbnailLoading = loading,
                token);
        }
    }

    private async Task LoadSectionThumbnailsAsync(CancellationToken token)
    {
        try
        {
            foreach (var item in TodayMemories.ToList())
            {
                token.ThrowIfCancellationRequested();
                await LoadThumbnailAsync(
                    item.MediaId,
                    item.AbsoluteLibraryPath,
                    image => item.ThumbnailImage = image,
                    loading => item.IsThumbnailLoading = loading,
                    token);
            }

            foreach (var item in RecentVisits.ToList())
            {
                token.ThrowIfCancellationRequested();
                await LoadThumbnailAsync(
                    item.RepresentativeMediaId,
                    item.AbsoluteLibraryPath,
                    image => item.ThumbnailImage = image,
                    loading => item.IsThumbnailLoading = loading,
                    token);
            }

            foreach (var item in Favorites.Concat(RecentImports).ToList())
            {
                token.ThrowIfCancellationRequested();
                await LoadThumbnailAsync(
                    item.MediaId,
                    item.AbsoluteLibraryPath,
                    image => item.ThumbnailImage = image,
                    loading => item.IsThumbnailLoading = loading,
                    token);
            }

            if (PendingSummary.RepresentativeMediaId is Guid pendingMediaId
                && !string.IsNullOrWhiteSpace(PendingSummary.RepresentativeAbsoluteLibraryPath))
            {
                token.ThrowIfCancellationRequested();
                await LoadThumbnailAsync(
                    pendingMediaId,
                    PendingSummary.RepresentativeAbsoluteLibraryPath,
                    image => PendingThumbnailImage = image,
                    _ => { },
                    token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on reload/unload.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Home section thumbnail loading failed.");
        }
    }

    private async Task LoadThumbnailAsync(
        Guid? mediaId,
        string absolutePath,
        Action<BitmapImage?> setImage,
        Action<bool> setLoading,
        CancellationToken token)
    {
        if (mediaId is null || string.IsNullOrWhiteSpace(absolutePath))
        {
            return;
        }

        setLoading(true);
        try
        {
            var path = await _thumbnailService.GetOrCreateThumbnailAsync(mediaId.Value, absolutePath, token);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            await EnqueueAsync(() => setImage(new BitmapImage(new Uri(path))));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Home thumbnail failed. MediaId={MediaId}", mediaId);
        }
        finally
        {
            setLoading(false);
        }
    }

    private void StartHeroTimer()
    {
        if (HeroMemories.Count > 1)
        {
            _heroPaused = false;
            _heroTimer.Start();
        }
    }

    private void StopHeroTimer()
    {
        _heroTimer.Stop();
        _heroPaused = false;
    }

    private void CancelThumbnailLoading()
    {
        if (_thumbnailCts is null)
        {
            return;
        }

        _thumbnailCts.Cancel();
        _thumbnailCts.Dispose();
        _thumbnailCts = null;
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // Reload cancelled.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Home dashboard load failed.");
            StatusMessage = "추억을 불러오지 못했어요. 잠시 후 다시 열어 보세요.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task EnqueueAsync(Action action)
    {
        var tcs = new TaskCompletionSource();
        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            tcs.SetResult();
        }

        return tcs.Task;
    }
}
