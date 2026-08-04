using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Navigation;
using MemoryKeeper.Application.Services;
using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingRepository _settingRepository;
    private readonly HomeLocationService _homeLocationService;
    private readonly IPrototypeMaintenanceService _maintenanceService;
    private readonly LibraryCopyIntegrityService _libraryCopyIntegrityService;
    private readonly PlaceRenormalizationService _placeRenormalizationService;
    private readonly IFileDialogService _fileDialogService;
    private readonly INavigationService _navigation;
    private readonly ILogger<SettingsViewModel> _logger;
    private CancellationTokenSource? _suggestCts;
    private int _suggestVersion;
    private bool _suppressHomeAddressSuggest;

    public StorageManagementViewModel Storage { get; }

    [ObservableProperty]
    private string activeSection = "overview";

    [ObservableProperty]
    private string breadcrumbTitle = "설정";

    [ObservableProperty]
    private bool isOverviewVisible = true;

    [ObservableProperty]
    private bool isStoragePanelVisible;

    [ObservableProperty]
    private bool isHomePanelVisible;

    [ObservableProperty]
    private bool isGooglePanelVisible;

    [ObservableProperty]
    private bool isGeneralVisible;

    [ObservableProperty]
    private bool isPhotoVisible;

    [ObservableProperty]
    private bool isAiVisible;

    [ObservableProperty]
    private bool isMaintenanceVisible;

    [ObservableProperty]
    private bool isInfoVisible;

    [ObservableProperty]
    private bool isTagsVisible;

    [ObservableProperty]
    private bool isLogsVisible;

    [ObservableProperty]
    private string googleMapsApiKey = string.Empty;

    [ObservableProperty]
    private string homeAddress = string.Empty;

    [ObservableProperty]
    private string homeLatitude = string.Empty;

    [ObservableProperty]
    private string homeLongitude = string.Empty;

    [ObservableProperty]
    private string homePlaceId = string.Empty;

    [ObservableProperty]
    private string homeResolvedSummary = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PlaceSuggestionDto> homeSuggestions = [];

    [ObservableProperty]
    private bool hasHomeSuggestions;

    [ObservableProperty]
    private string statusMessage = "설정을 관리합니다.";

    [ObservableProperty]
    private string infoBarMessage = string.Empty;

    [ObservableProperty]
    private bool isInfoBarOpen;

    [ObservableProperty]
    private InfoBarSeverity infoBarSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string logText = string.Empty;

    [ObservableProperty]
    private string appVersionText = string.Empty;

    public XamlRoot? HostXamlRoot { get; set; }

    public event EventHandler? OpenImportRequested;

    public event EventHandler? OpenPlaceRequested;

    public event EventHandler? OpenPendingRequested;

    public event EventHandler? TagsSectionOpened;

    public SettingsViewModel(
        ISettingRepository settingRepository,
        HomeLocationService homeLocationService,
        IPrototypeMaintenanceService maintenanceService,
        LibraryCopyIntegrityService libraryCopyIntegrityService,
        PlaceRenormalizationService placeRenormalizationService,
        IFileDialogService fileDialogService,
        StorageManagementViewModel storageSettings,
        INavigationService navigation,
        ILogger<SettingsViewModel> logger)
    {
        _settingRepository = settingRepository;
        _homeLocationService = homeLocationService;
        _maintenanceService = maintenanceService;
        _libraryCopyIntegrityService = libraryCopyIntegrityService;
        _placeRenormalizationService = placeRenormalizationService;
        _fileDialogService = fileDialogService;
        Storage = storageSettings;
        _navigation = navigation;
        _logger = logger;
        AppVersionText = GetAppVersion();
    }

    [RelayCommand]
    private async Task LoadAsync(string? section)
    {
        IsBusy = true;
        try
        {
            await Storage.LoadCommand.ExecuteAsync(null);

            var apiKey = await _settingRepository.GetByKeyAsync(SettingKeys.GoogleMapsApiKey);
            GoogleMapsApiKey = apiKey?.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(GoogleMapsApiKey)
                && !GoogleMapsApiKeyValidator.LooksValid(GoogleMapsApiKey))
            {
                ShowError(
                    "저장된 Google API Key 형식이 올바르지 않습니다. 설정 → Google API에서 AIza… Key를 다시 저장하세요.");
            }

            var home = await _homeLocationService.GetAsync();
            ApplyHome(home);

            var target = string.IsNullOrWhiteSpace(section) ? "overview" : section;
            ShowSection(target);
            StatusMessage = "설정을 확인하세요.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings.");
            ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ShowStorage() => ShowSection("storage");

    [RelayCommand]
    private void ShowHome() => ShowSection("home");

    [RelayCommand]
    private void ShowGoogleApi() => ShowSection("google");

    [RelayCommand]
    private void ShowPhotoImport() => ShowSection("photo-import");

    [RelayCommand]
    private void ShowMetadata() => ShowSection("photo-import");

    [RelayCommand]
    private void ShowTags()
    {
        ShowSection("tags");
        TagsSectionOpened?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowAi() => ShowSection("ai");

    [RelayCommand]
    private void ShowMaintenance() => ShowSection("maintenance");

    [RelayCommand]
    private void ShowInfo() => ShowSection("info");

    [RelayCommand]
    private void ShowLogs()
    {
        ShowSection("logs");
        RefreshLogs();
    }

    [RelayCommand]
    private void GoBack() => ShowSection("overview");

    [RelayCommand]
    private void RefreshLogs()
    {
        try
        {
            var path = StartupDiagnostics.LogFilePath;
            LogText = File.Exists(path)
                ? File.ReadAllText(path)
                : "아직 기록된 로그가 없습니다.";
        }
        catch (Exception ex)
        {
            LogText = $"로그를 읽지 못했습니다: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenImport() => OpenImportRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenPlace() => OpenPlaceRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenPending() => OpenPendingRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task ChangeStorageFolderAsync()
    {
        await RunBusyAsync(async () =>
        {
            await Storage.ChangeFolderCommand.ExecuteAsync(null);
            if (Storage.HasCheckedConnection && Storage.FolderExists && Storage.IsReadable && Storage.IsWritable)
            {
                ShowSuccess("MemoryKeeper 저장소가 정상적으로 연결되었습니다.");
            }
            else if (!string.IsNullOrWhiteSpace(Storage.StatusMessage))
            {
                ShowError(Storage.StatusMessage);
            }
        });
    }

    [RelayCommand]
    private async Task CheckStorageConnectionAsync()
    {
        await RunBusyAsync(async () =>
        {
            await Storage.CheckConnectionCommand.ExecuteAsync(null);
            if (Storage.HasCheckedConnection && Storage.FolderExists && Storage.IsReadable && Storage.IsWritable)
            {
                ShowSuccess("MemoryKeeper 저장소가 정상적으로 연결되었습니다.");
            }
            else
            {
                ShowError(string.IsNullOrWhiteSpace(Storage.StatusMessage)
                    ? "저장소 연결을 확인할 수 없습니다."
                    : Storage.StatusMessage);
            }
        });
    }

    [RelayCommand]
    private async Task SaveApiKeyAsync()
    {
        await RunBusyAsync(async () =>
        {
            var value = GoogleMapsApiKey.Trim();
            GoogleMapsApiKeyValidator.EnsureValidOrEmpty(value);

            var existing = await _settingRepository.GetByKeyAsync(SettingKeys.GoogleMapsApiKey);
            var now = DateTime.UtcNow;
            if (existing is null)
            {
                await _settingRepository.AddAsync(new Domain.Entities.Setting
                {
                    Id = Guid.NewGuid(),
                    Key = SettingKeys.GoogleMapsApiKey,
                    Value = value,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                existing.Value = value;
                existing.UpdatedAt = now;
                await _settingRepository.UpdateAsync(existing);
            }

            ShowSuccess(string.IsNullOrWhiteSpace(value)
                ? "API Key를 비웠습니다. 지도·주소 검색이 비활성화됩니다."
                : "Google API Key를 저장했습니다. 지도와 주소 검색에 바로 사용됩니다.");
        });
    }

    partial void OnHomeAddressChanged(string value)
    {
        if (_suppressHomeAddressSuggest)
        {
            return;
        }

        // Manual edit invalidates the previously selected PlaceId.
        HomePlaceId = string.Empty;
        _ = SuggestHomePlacesAsync(value);
    }

    [RelayCommand]
    private async Task SelectHomeSuggestionAsync(PlaceSuggestionDto? suggestion)
    {
        if (suggestion is null || string.IsNullOrWhiteSpace(suggestion.PlaceId))
        {
            return;
        }

        CancelHomeSuggestions();
        await RunBusyAsync(async () =>
        {
            var saved = await _homeLocationService.SavePlaceSelectionAsync(suggestion.PlaceId);
            ApplyHome(saved);
            HomeSuggestions.Clear();
            HasHomeSuggestions = false;
            ShowSuccess("집(Home) 위치를 저장했습니다.");
        });
    }

    [RelayCommand]
    private async Task SaveHomeAsync()
    {
        CancelHomeSuggestions();
        await RunBusyAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(HomePlaceId))
            {
                var saved = await _homeLocationService.SavePlaceSelectionAsync(HomePlaceId);
                ApplyHome(saved);
                ShowSuccess("집(Home) 위치를 저장했습니다.");
                return;
            }

            if (string.IsNullOrWhiteSpace(HomeAddress))
            {
                throw new InvalidOperationException("주소를 입력한 뒤 자동완성에서 장소를 선택하세요.");
            }

            var byAddress = await _homeLocationService.SaveAddressAsync(HomeAddress);
            ApplyHome(byAddress);
            ShowSuccess("집(Home) 위치를 저장했습니다.");
        });
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        var suggested = $"MemoryKeeperBackup_{DateTime.Now:yyyyMMdd}.zip";
        var path = await _fileDialogService.PickSaveZipAsync(suggested);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _maintenanceService.BackupAsync(path);
            if (result.Succeeded)
            {
                ShowSuccess("백업이 완료되었습니다.");
            }
            else
            {
                ShowError(result.Message);
            }
        });
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (!await UserFeedback.ConfirmAsync(
                HostXamlRoot,
                "복원 확인",
                "현재 데이터베이스를 백업 zip으로 덮어씁니다. 계속할까요?",
                primaryText: "복원"))
        {
            return;
        }

        var path = await _fileDialogService.PickOpenZipAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _maintenanceService.RestoreAsync(path, backupExistingDatabase: true);
            if (result.Succeeded)
            {
                ShowSuccess(result.Message);
            }
            else
            {
                ShowError(result.Message);
            }
        });
    }

    [RelayCommand]
    private async Task RenormalizePlacesAsync()
    {
        if (!await UserFeedback.ConfirmAsync(
                HostXamlRoot,
                "장소 재정규화",
                "모든 장소의 Canonical Name을 다시 계산하고, Osaka/Osaka-shi/大阪市처럼 동일 지역을 하나로 병합합니다.\n연결된 사진도 대표 장소로 재연결됩니다.",
                primaryText: "재정규화"))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _placeRenormalizationService.RenormalizeAndMergeAsync();
            if (result.Succeeded)
            {
                ShowSuccess(result.Message);
            }
            else
            {
                ShowError(result.Message);
            }
        });
    }

    [RelayCommand]
    private async Task InspectLibraryCopiesAsync()
    {
        if (!await UserFeedback.ConfirmAsync(
                HostXamlRoot,
                "사본 무결성 검사",
                "동일 Hash의 중복 사본을 검사하고, 발견 시 대표 파일 1개만 남기고 나머지를 삭제합니다.\nRelativePath도 실제 파일에 맞게 복구합니다.",
                primaryText: "검사·복구"))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _libraryCopyIntegrityService.InspectAndRepairAsync();
            if (result.Succeeded)
            {
                ShowSuccess(result.Message);
            }
            else
            {
                ShowError(result.Message);
            }
        });
    }

    [RelayCommand]
    private async Task ClearImportDataAsync()
    {
        if (!await UserFeedback.ConfirmAsync(
                HostXamlRoot,
                "등록사진 초기화",
                "등록된 사진 메타데이터(Media/Tag/Place)를 삭제합니다.\n사진 원본과 Google API Key는 유지됩니다.",
                primaryText: "초기화"))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _maintenanceService.ClearImportDataAsync();
            ShowSuccess(result.Message);
        });
    }

    [RelayCommand]
    private async Task RegeneratePlacesAsync()
    {
        if (!await UserFeedback.ConfirmAsync(
                HostXamlRoot,
                "장소/여행기록 재생성",
                "기존 장소를 모두 지운 뒤, GPS가 있는 사진마다 장소를 다시 만들고 연결합니다.\n방문지도·여행기록은 장소/사진 기준으로 다시 집계됩니다.\nGoogle API Key는 유지됩니다.",
                primaryText: "재생성"))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _maintenanceService.RegeneratePlacesAsync();
            ShowSuccess(result.Message);
        });
    }

    [RelayCommand]
    private async Task ClearThumbnailCacheAsync()
    {
        if (!await UserFeedback.ConfirmAsync(
                HostXamlRoot,
                "썸네일 캐시 삭제",
                "썸네일 캐시 파일을 삭제합니다. 사진 원본과 API Key는 유지됩니다.",
                primaryText: "삭제"))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _maintenanceService.ClearThumbnailCacheAsync();
            ShowSuccess(result.Message);
        });
    }

    [RelayCommand]
    private async Task ResetDatabaseAsync()
    {
        if (!await UserFeedback.ConfirmAsync(
                HostXamlRoot,
                "전체 초기화",
                "Database·Settings·Thumbnail을 초기화합니다.\nGoogle API Key도 삭제됩니다.\n사진 원본은 유지됩니다.",
                primaryText: "전체 초기화"))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _maintenanceService.ResetDatabaseAsync();
            ShowSuccess(result.Message + " 앱을 다시 시작하면 초기 설정이 표시됩니다.");
        });
    }

    private async Task SuggestHomePlacesAsync(string input)
    {
        var version = Interlocked.Increment(ref _suggestVersion);
        _suggestCts?.Cancel();
        _suggestCts?.Dispose();
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
        }
        catch (OperationCanceledException)
        {
            // ignore debounce cancel
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Home place suggestion failed.");
            HomeSuggestions.Clear();
            HasHomeSuggestions = false;
            ShowError(ex.Message);
        }
    }

    private void ShowSection(string section)
    {
        ActiveSection = section;
        IsOverviewVisible = section == "overview";
        IsStoragePanelVisible = section == "storage";
        IsHomePanelVisible = section == "home";
        IsGooglePanelVisible = section == "google";
        IsGeneralVisible = section is "storage" or "home" or "google";
        IsPhotoVisible = section is "photo-import" or "metadata";
        IsAiVisible = section == "ai";
        IsMaintenanceVisible = section == "maintenance";
        IsInfoVisible = section is "info" or "logs";
        IsTagsVisible = section == "tags";
        IsLogsVisible = section == "logs";
        BreadcrumbTitle = section switch
        {
            "overview" => "설정",
            "storage" => "설정 › MemoryKeeper 저장소",
            "home" => "설정 › 집(Home) 위치",
            "google" => "설정 › Google API",
            "photo-import" or "metadata" => "설정 › 사진관리",
            "tags" => "설정 › 태그관리",
            "ai" => "설정 › AI",
            "maintenance" => "설정 › 유지보수",
            "info" => "설정 › 프로그램 정보",
            "logs" => "설정 › 로그 보기",
            _ => "설정"
        };

        // Keep shell back-stack section in sync so child pages return here.
        if (_navigation.Current?.Tag is "settings" or null)
        {
            _navigation.ReplaceCurrent(NavigationEntry.Of("settings", section));
        }
    }

    private void ApplyHome(HomeLocationDto home)
    {
        _suppressHomeAddressSuggest = true;
        try
        {
            HomeAddress = home.Address;
            HomePlaceId = home.PlaceId;
            HomeLatitude = home.Latitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            HomeLongitude = home.Longitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            HomeResolvedSummary = home.IsConfigured
                ? $"{home.Address}\n위도 {HomeLatitude} / 경도 {HomeLongitude}"
                : "아직 집 위치가 설정되지 않았습니다.";
        }
        finally
        {
            _suppressHomeAddressSuggest = false;
        }
    }

    private void CancelHomeSuggestions()
    {
        Interlocked.Increment(ref _suggestVersion);
        _suggestCts?.Cancel();
        HomeSuggestions.Clear();
        HasHomeSuggestions = false;
    }

    private void ShowSuccess(string message)
    {
        StatusMessage = message;
        InfoBarMessage = message;
        InfoBarSeverity = InfoBarSeverity.Success;
        IsInfoBarOpen = true;
    }

    private void ShowError(string message)
    {
        StatusMessage = message;
        InfoBarMessage = message;
        InfoBarSeverity = InfoBarSeverity.Error;
        IsInfoBarOpen = true;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings action failed.");
            ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string GetAppVersion()
    {
        try
        {
            var version = typeof(SettingsViewModel).Assembly.GetName().Version;
            return version is null ? "unknown" : version.ToString();
        }
        catch
        {
            return "unknown";
        }
    }
}
