using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private static readonly TimeSpan HeroInterval = TimeSpan.FromSeconds(4);

    private readonly IGalleryApiRepository _galleryApiRepository;
    private readonly IFastGalleryApiRepository _fastGallery;
    private readonly ITravelRecordsRepository _travelRecordsRepository;
    private readonly BaseApiClient _apiClient;
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
    private int _dashboardGeneration;

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
    private ObservableCollection<HomeYearBarItem> yearBars = [];

    [ObservableProperty]
    private ObservableCollection<HomeCountrySliceItem> countrySlices = [];

    [ObservableProperty]
    private bool hasYearChart;

    [ObservableProperty]
    private bool hasCountryChart;

    [ObservableProperty]
    private string photoCountDisplay = "0장";

    [ObservableProperty]
    private string gpsCountDisplay = "0장";

    [ObservableProperty]
    private string placeCountDisplay = "0곳";

    [ObservableProperty]
    private string countryCountDisplay = "0개국";

    [ObservableProperty]
    private string countrySummaryText = string.Empty;

    [ObservableProperty]
    private string lastUpdatedDisplay = string.Empty;

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
    private string pendingQuickActionText = "정보 보완이 필요한 사진 · 0장";

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
        IFastGalleryApiRepository fastGallery,
        ITravelRecordsRepository travelRecordsRepository,
        BaseApiClient apiClient,
        IThumbnailService thumbnailService,
        IPlaceFocusState placeFocusState,
        IPhotoNavigationState photoNavigationState,
        ITravelRecordsNavigationState travelRecordsNavigationState,
        ILogger<HomeViewModel> logger)
    {
        _galleryApiRepository = galleryApiRepository;
        _fastGallery = fastGallery;
        _travelRecordsRepository = travelRecordsRepository;
        _apiClient = apiClient;
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
        // MainWindow navigation and Page.Loaded can request Home at nearly the same time.
        // Do not let the second request cancel the thumbnail token owned by the active load.
        if (IsBusy)
        {
            return;
        }

        StopHeroTimer();
        CancelThumbnailLoading();
        var generation = Interlocked.Increment(ref _dashboardGeneration);
        _thumbnailCts = new CancellationTokenSource();
        var token = _thumbnailCts.Token;

        await RunBusyAsync(async () =>
        {
            ClearLocalDashboardSections();
            try
            {
                var dashboard = await GalleryBackendBridge.GetFastHomeDashboardAsync(
                    _fastGallery,
                    _apiClient.ApiBaseUrl,
                    _logger,
                    token);
                ApplyDashboard(dashboard);
                StatusMessage = "추억을 불러왔습니다.";
                _ = RefreshAuthoritativePlacesAsync(dashboard, generation, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Home dashboard failed; falling back to statistics.");
                try
                {
                    Statistics = await GalleryBackendBridge.GetStatisticsAsync(_galleryApiRepository, token);
                    UpdateStatisticsSummary();
                    StatusMessage = "통계만 불러왔습니다.";
                }
                catch (Exception statsEx)
                {
                    _logger.LogWarning(statsEx, "Backend statistics failed.");
                    Statistics = new DashboardStatisticsDto();
                    StatusMessage = $"추억을 불러오지 못했습니다. {ex.Message}";
                }
            }
        });

        if (token.IsCancellationRequested)
        {
            return;
        }

        // Hero and section images are optional visual enrichment.  Start both immediately
        // after the dashboard shell is visible; recent-photo thumbnails must not wait for
        // the hero carousel's sequential preloading.
        _ = PreloadHeroThumbnailsAsync(token);
        _ = LoadSectionThumbnailsAsync(token);
        StartHeroTimer();
    }

    private async Task RefreshAuthoritativePlacesAsync(
        HomeDashboardDto initialDashboard,
        int generation,
        CancellationToken token)
    {
        try
        {
            var placeAggregates = await _travelRecordsRepository.GetPlaceAggregatesAsync(token);
            if (token.IsCancellationRequested || generation != Volatile.Read(ref _dashboardGeneration))
            {
                return;
            }

            ApplyDashboard(GalleryBackendBridge.ApplyAuthoritativePlaceAggregates(initialDashboard, placeAggregates));
            _ = PreloadHeroThumbnailsAsync(token);
            _logger.LogDebug(
                "Home authoritative place aggregate applied. Generation={Generation}, Places={PlaceCount}",
                generation,
                Statistics.PlaceCount);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer Home generation owns the screen.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Home authoritative place aggregate unavailable; retaining Fast Gallery shell.");
        }
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
    private void QuickPending() => OpenPendingRequested?.Invoke(this, EventArgs.Empty);

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
        PendingQuickActionText = "정보 보완이 필요한 사진 · 0장";
        PendingLatestImportedText = string.Empty;
        PendingThumbnailImage = null;
        StatisticsSummaryText = "시간을 따라 여행을 다시 만나 보세요.";
        YearBars = [];
        CountrySlices = [];
        HasYearChart = false;
        HasCountryChart = false;
        PhotoCountDisplay = "0장";
        GpsCountDisplay = "0장";
        PlaceCountDisplay = "0곳";
        CountryCountDisplay = "0개국";
        CountrySummaryText = string.Empty;
        LastUpdatedDisplay = string.Empty;
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
            $"사진 {Statistics.PhotoCount} · 장소 {Statistics.PlaceCount} · 국가 {Statistics.CountryCount}";
        PhotoCountDisplay = $"{Statistics.PhotoCount:N0}장";
        GpsCountDisplay = $"{Statistics.GpsCount:N0}장";
        PlaceCountDisplay = $"{Statistics.PlaceCount:N0}곳";
        CountryCountDisplay = $"{Statistics.CountryCount:N0}개국";
        CountrySummaryText = string.IsNullOrWhiteSpace(Statistics.CountrySummary)
            ? "기록된 국가 없음"
            : Statistics.CountrySummary;
        LastUpdatedDisplay = string.IsNullOrWhiteSpace(Statistics.LastUpdatedText)
            ? DateTime.Now.ToString("yyyy.MM.dd")
            : Statistics.LastUpdatedText;
        RebuildCharts();
    }

    private void RebuildCharts()
    {
        const double maxBarHeight = 120d;
        var years = Statistics.ByYear.Where(x => x.Count > 0).ToList();
        var maxYear = years.Count == 0 ? 0 : years.Max(x => x.Count);
        YearBars = new ObservableCollection<HomeYearBarItem>(
            years.Select(x =>
            {
                var ratio = maxYear <= 0 ? 0 : (double)x.Count / maxYear;
                return new HomeYearBarItem(x.Name, x.Count, ratio, Math.Max(6, ratio * maxBarHeight));
            }));
        HasYearChart = YearBars.Count > 0;

        var colors = new[]
        {
            Windows.UI.Color.FromArgb(255, 0, 122, 255),
            Windows.UI.Color.FromArgb(255, 52, 199, 89),
            Windows.UI.Color.FromArgb(255, 255, 149, 0),
            Windows.UI.Color.FromArgb(255, 175, 82, 222),
            Windows.UI.Color.FromArgb(255, 90, 200, 250),
            Windows.UI.Color.FromArgb(255, 255, 59, 48),
        };

        var countries = Statistics.ByCountry.Where(x => x.Count > 0).Take(6).ToList();
        var total = countries.Sum(x => x.Count);
        var slices = new List<HomeCountrySliceItem>();
        var angle = -90d;
        for (var i = 0; i < countries.Count; i++)
        {
            var item = countries[i];
            var sweep = total <= 0 ? 0 : 360d * item.Count / total;
            if (i == countries.Count - 1)
            {
                sweep = 270 - angle; // close the circle (-90 + 360 = 270)
            }

            slices.Add(new HomeCountrySliceItem(item.Name, item.Count, angle, Math.Max(0.1, sweep), colors[i % colors.Length]));
            angle += sweep;
        }

        CountrySlices = new ObservableCollection<HomeCountrySliceItem>(slices);
        HasCountryChart = CountrySlices.Count > 0;
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
        PendingQuickActionText = $"정보 보완이 필요한 사진 · {PendingSummary.Total:N0}장";
        PendingLatestImportedText = PendingSummary.LatestImportedAt.HasValue
            ? $"최근 담은 날 {PendingSummary.LatestImportedAt.Value.ToLocalTime():yyyy.MM.dd}"
            : string.Empty;
        PendingThumbnailImage = null;
        UpdateStatisticsSummary();

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
                    token,
                    item.FallbackAbsoluteLibraryPath);
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
        CancellationToken token,
        string? fallbackAbsolutePath = null)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return;
        }

        setLoading(true);
        try
        {
            if (HttpImageLoader.IsHttpUrl(absolutePath))
            {
                var bitmap = await HttpImageLoader.LoadFirstAvailableAsync(
                    [absolutePath, fallbackAbsolutePath],
                    _logger,
                    $"home:{mediaId}",
                    token);
                await EnqueueAsync(() => setImage(bitmap));
                return;
            }

            if (mediaId is null)
            {
                return;
            }

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
