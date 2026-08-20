using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.App.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly SetupWizardService _setupWizardService;
    private readonly HomeLocationService _homeLocationService;
    private readonly ILogger<SetupWizardViewModel> _logger;
    private CancellationTokenSource? _suggestCts;
    private int _suggestVersion;
    private bool _suppressSuggestions;

    [ObservableProperty]
    private int currentStep = 1;

    [ObservableProperty]
    private string homeAddress = string.Empty;

    [ObservableProperty]
    private string homeResolvedSummary = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PlaceSuggestionDto> homeSuggestions = [];

    [ObservableProperty]
    private bool hasHomeSuggestions;

    [ObservableProperty]
    private bool hasSelectedHome;

    [ObservableProperty]
    private string statusMessage = "집으로 사용할 주소나 장소를 검색하세요.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool canFinish;

    public event EventHandler? SetupCompleted;

    public SetupWizardViewModel(
        SetupWizardService setupWizardService,
        HomeLocationService homeLocationService,
        ILogger<SetupWizardViewModel> logger)
    {
        _setupWizardService = setupWizardService;
        _homeLocationService = homeLocationService;
        _logger = logger;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var home = await _homeLocationService.GetAsync();
            if (home.IsConfigured)
            {
                ApplyHome(home);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Existing home location could not be loaded in setup.");
            StatusMessage = "저장된 집 위치를 불러오지 못했습니다. 다시 검색해 주세요.";
        }
    }

    partial void OnHomeAddressChanged(string value)
    {
        if (_suppressSuggestions)
        {
            return;
        }

        HasSelectedHome = false;
        HomeResolvedSummary = string.Empty;
        _ = SuggestHomePlacesAsync(value);
    }

    [RelayCommand]
    private async Task SelectHomeSuggestionAsync(PlaceSuggestionDto? suggestion)
    {
        if (suggestion is null || string.IsNullOrWhiteSpace(suggestion.PlaceId) || IsBusy)
        {
            return;
        }

        CancelSuggestions();
        IsBusy = true;
        try
        {
            var saved = await _homeLocationService.SavePlaceSelectionAsync(suggestion.PlaceId);
            ApplyHome(saved);
            HomeSuggestions.Clear();
            HasHomeSuggestions = false;
            StatusMessage = "집 위치를 선택했습니다.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Home place selection failed during setup.");
            StatusMessage = "선택한 장소를 저장하지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ChangeHome()
    {
        HasSelectedHome = false;
        HomeResolvedSummary = string.Empty;
        StatusMessage = "새 집 위치를 검색하세요.";
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (IsBusy || CurrentStep != 1)
        {
            return;
        }

        if (!HasSelectedHome)
        {
            StatusMessage = "검색 결과에서 집 위치를 선택하세요.";
            return;
        }

        var status = await _setupWizardService.GetStatusAsync();
        if (!status.HasHomeLocation)
        {
            StatusMessage = "집 위치를 저장하지 못했습니다. 다시 선택해 주세요.";
            return;
        }

        CurrentStep = 2;
        CanFinish = true;
        StatusMessage = "준비가 끝났습니다. 바로 MemoryKeeper를 시작할 수 있습니다.";
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep == 2 && !IsBusy)
        {
            CurrentStep = 1;
            CanFinish = false;
            StatusMessage = "선택한 집 위치를 확인하거나 변경하세요.";
        }
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (IsBusy || !CanFinish)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _setupWizardService.MarkSetupCompletedAsync();
            SetupCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark setup complete.");
            StatusMessage = "초기 설정을 완료하지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SuggestHomePlacesAsync(string input)
    {
        var version = Interlocked.Increment(ref _suggestVersion);
        CancelSuggestions();
        _suggestCts = new CancellationTokenSource();
        var token = _suggestCts.Token;

        try
        {
            await Task.Delay(350, token);
            if (version != _suggestVersion)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(input) || input.Trim().Length < 2)
            {
                HomeSuggestions.Clear();
                HasHomeSuggestions = false;
                return;
            }

            var suggestions = await _homeLocationService.SuggestPlacesAsync(input.Trim(), token);
            if (version != _suggestVersion)
            {
                return;
            }

            HomeSuggestions = new ObservableCollection<PlaceSuggestionDto>(suggestions);
            HasHomeSuggestions = HomeSuggestions.Count > 0;
            StatusMessage = HasHomeSuggestions
                ? "검색 결과에서 집 위치를 선택하세요."
                : "검색 결과가 없습니다. 다른 주소나 장소명으로 검색해 주세요.";
        }
        catch (OperationCanceledException)
        {
            // Debounce cancellation is expected.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Home place suggestions failed during setup.");
            HomeSuggestions.Clear();
            HasHomeSuggestions = false;
            StatusMessage = "주소 검색에 연결할 수 없습니다. 잠시 후 다시 시도해 주세요.";
        }
    }

    private void ApplyHome(HomeLocationDto home)
    {
        _suppressSuggestions = true;
        HomeAddress = home.Address;
        _suppressSuggestions = false;
        HomeResolvedSummary = string.IsNullOrWhiteSpace(home.Address) ? "저장된 집 위치" : home.Address;
        HasSelectedHome = home.IsConfigured;
    }

    private void CancelSuggestions()
    {
        _suggestCts?.Cancel();
        _suggestCts?.Dispose();
        _suggestCts = null;
    }
}
