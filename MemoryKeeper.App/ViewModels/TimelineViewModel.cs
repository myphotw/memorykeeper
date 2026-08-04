using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace MemoryKeeper.App.ViewModels;

public partial class TimelineViewModel : ObservableObject
{
    private readonly MemorySearchService _memorySearchService;
    private readonly IPlaceFocusState _placeFocusState;
    private readonly IPhotoNavigationState _photoNavigationState;
    private readonly ILogger<TimelineViewModel> _logger;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _suggestCts;

    [ObservableProperty]
    private ObservableCollection<int> availableYears = [];

    [ObservableProperty]
    private int? selectedYear;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SearchChipItem> searchChips = [];

    [ObservableProperty]
    private ObservableCollection<SearchSuggestionItem> suggestions = [];

    [ObservableProperty]
    private ObservableCollection<string> recentQueries = [];

    [ObservableProperty]
    private bool isSuggestionOpen;

    [ObservableProperty]
    private bool isRecentOpen;

    [ObservableProperty]
    private bool hasNoResults;

    [ObservableProperty]
    private ObservableCollection<TimelineYearGroup> yearGroups = [];

    [ObservableProperty]
    private ObservableCollection<TimelinePlaceItem> results = [];

    [ObservableProperty]
    private TimelinePlaceItem? selectedPlace;

    [ObservableProperty]
    private Guid? selectedPlaceId;

    [ObservableProperty]
    private string statusMessage = "기억나는 단어를 입력해 검색하세요.";

    [ObservableProperty]
    private bool isBusy;

    public event EventHandler<Guid>? PlaceSelected;

    public event EventHandler? OpenMapRequested;

    public event EventHandler? OpenGalleryRequested;

    public TimelineViewModel(
        MemorySearchService memorySearchService,
        IPlaceFocusState placeFocusState,
        IPhotoNavigationState photoNavigationState,
        ILogger<TimelineViewModel> logger)
    {
        _memorySearchService = memorySearchService;
        _placeFocusState = placeFocusState;
        _photoNavigationState = photoNavigationState;
        _logger = logger;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        AvailableYears = new ObservableCollection<int>(BuildDefaultYears());
    }

    partial void OnSelectedPlaceChanged(TimelinePlaceItem? value)
    {
        SelectedPlaceId = value?.PlaceId;
        if (value is not null)
        {
            _placeFocusState.FocusPlaceId = value.PlaceId;
            PlaceSelected?.Invoke(this, value.PlaceId);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = RefreshSuggestionsAsync(value);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RefreshRecentQueriesAsync();
        await SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await RunBusyAsync(async () =>
        {
            IsSuggestionOpen = false;
            IsRecentOpen = false;

            var request = string.IsNullOrWhiteSpace(SearchText)
                ? new MemorySearchRequest { Year = SelectedYear }
                : new MemorySearchRequest { SearchText = SearchText.Trim() };

            var queryResult = await _memorySearchService.SearchAsync(request);
            ApplyQueryResult(queryResult);
            await RefreshRecentQueriesAsync();
        });
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        SelectedYear = null;
        SearchChips = [];
        Suggestions = [];
        IsSuggestionOpen = false;
        HasNoResults = false;
        await SearchAsync();
        StatusMessage = "검색 조건을 초기화했습니다.";
    }

    [RelayCommand]
    private void ClearYearFilter()
    {
        SelectedYear = null;
    }

    [RelayCommand]
    private async Task ApplySuggestionAsync(SearchSuggestionItem? suggestion)
    {
        if (suggestion is null)
        {
            return;
        }

        SearchText = ReplaceLastToken(SearchText, suggestion.Text);
        IsSuggestionOpen = false;
        await SearchAsync();
    }

    [RelayCommand]
    private async Task ApplyRecentQueryAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        SearchText = query;
        IsRecentOpen = false;
        await SearchAsync();
    }

    [RelayCommand]
    private async Task ShowRecentAsync()
    {
        await RefreshRecentQueriesAsync();
        IsSuggestionOpen = false;
        IsRecentOpen = RecentQueries.Count > 0;
    }

    [RelayCommand]
    private void SelectPlace(TimelinePlaceItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedPlace = item;
    }

    [RelayCommand]
    private void OpenPhotoDetail(TimelinePlaceItem? item)
    {
        if (item?.RepresentativeMediaId is not Guid mediaId)
        {
            StatusMessage = "이 장소에서 열 수 있는 사진이 없습니다.";
            return;
        }

        SelectedPlace = item;
        _photoNavigationState.RequestOpen(mediaId);
    }

    [RelayCommand(CanExecute = nameof(CanOpenMap))]
    private void OpenMap()
    {
        if (SelectedPlaceId is not Guid placeId)
        {
            return;
        }

        _placeFocusState.FocusPlaceId = placeId;
        OpenMapRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanOpenGallery))]
    private void OpenGallery()
    {
        if (SelectedPlaceId is not Guid placeId)
        {
            return;
        }

        _placeFocusState.FocusPlaceId = placeId;
        OpenGalleryRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanOpenMap() => SelectedPlaceId.HasValue;

    private bool CanOpenGallery() => SelectedPlaceId.HasValue;

    partial void OnSelectedPlaceIdChanged(Guid? value)
    {
        OpenMapCommand.NotifyCanExecuteChanged();
        OpenGalleryCommand.NotifyCanExecuteChanged();
    }

    private void ApplyQueryResult(MemorySearchQueryResult queryResult)
    {
        var items = queryResult.Items
            .Select(result => new TimelinePlaceItem(result))
            .ToList();

        Results = new ObservableCollection<TimelinePlaceItem>(items);
        YearGroups = new ObservableCollection<TimelineYearGroup>(BuildYearGroups(items));
        SearchChips = new ObservableCollection<SearchChipItem>(
            queryResult.Chips.Select(chip => new SearchChipItem(chip)));
        SelectedPlace = null;
        SelectedPlaceId = null;
        HasNoResults = items.Count == 0;

        if (items.Count == 0)
        {
            StatusMessage = "검색 결과가 없습니다.";
            return;
        }

        StatusMessage = string.IsNullOrWhiteSpace(SearchText)
            ? SelectedYear is null
                ? $"전체 {items.Count}개 장소를 표시합니다."
                : $"{SelectedYear}년 장소 {items.Count}개를 표시합니다."
            : $"'{SearchText.Trim()}' 검색 결과 {items.Count}개 장소";
    }

    private async Task RefreshSuggestionsAsync(string text)
    {
        _suggestCts?.Cancel();
        _suggestCts?.Dispose();
        _suggestCts = new CancellationTokenSource();
        var token = _suggestCts.Token;

        try
        {
            await Task.Delay(180, token);
            if (string.IsNullOrWhiteSpace(text))
            {
                await EnqueueAsync(() =>
                {
                    Suggestions = [];
                    IsSuggestionOpen = false;
                });
                return;
            }

            var items = await _memorySearchService.SuggestAsync(text, token);
            await EnqueueAsync(() =>
            {
                Suggestions = new ObservableCollection<SearchSuggestionItem>(
                    items.Select(item => new SearchSuggestionItem(item)));
                IsSuggestionOpen = Suggestions.Count > 0 && !IsBusy;
                IsRecentOpen = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Expected while typing.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Search suggestion failed.");
        }
    }

    private async Task RefreshRecentQueriesAsync()
    {
        var queries = await _memorySearchService.GetRecentQueriesAsync();
        RecentQueries = new ObservableCollection<string>(queries);
    }

    private static string ReplaceLastToken(string text, string replacement)
    {
        var trimmed = text.TrimEnd();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return replacement;
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return replacement;
        }

        parts[^1] = replacement;
        return string.Join(' ', parts);
    }

    private static IEnumerable<TimelineYearGroup> BuildYearGroups(IEnumerable<TimelinePlaceItem> items)
    {
        return items
            .GroupBy(item => (item.Result.LastCapturedDate ?? item.Result.FirstCapturedDate)?.Year ?? 0)
            .OrderByDescending(group => group.Key)
            .Select(group => new TimelineYearGroup(group.Key, group.OrderByDescending(item => item.Result.LastCapturedDate)));
    }

    private static IEnumerable<int> BuildDefaultYears()
    {
        var currentYear = DateTime.Now.Year;
        for (var year = currentYear; year >= currentYear - 30; year--)
        {
            yield return year;
        }
    }

    private Task EnqueueAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
            tcs.SetException(new InvalidOperationException("Failed to enqueue timeline UI update."));
        }

        return tcs.Task;
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Timeline search failed.");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
