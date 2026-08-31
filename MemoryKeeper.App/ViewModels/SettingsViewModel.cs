using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Navigation;
using MemoryKeeper.Application.Services;
using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Services;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.App.ViewModels;

public enum SettingsSection
{
    PhotoManagement,
    PendingMemories,
    Places,
    Tags,
    HomeLocation,
    AutoTags,
    PhotoExport,
    PreviewCache,
    Reset,
    AppInfo,
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly HomeLocationService _homeLocationService;
    private readonly MemoryKeeperOperationsService _operationsService;
    private readonly PhotoExportService _photoExportService;
    private readonly ILocalPreviewCacheService _previewCacheService;
    private readonly IFolderPickerService _folderPickerService;
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
    private bool isLogsVisible;

    [ObservableProperty]
    private bool isSettingsDashboardVisible = true;

    [ObservableProperty]
    private SettingsSection selectedSettingsSection = SettingsSection.PhotoManagement;

    public bool IsPhotoManagementDetailVisible => SelectedSettingsSection == SettingsSection.PhotoManagement;
    public bool IsPendingMemoriesDetailVisible => SelectedSettingsSection == SettingsSection.PendingMemories;
    public bool IsPlacesDetailVisible => SelectedSettingsSection == SettingsSection.Places;
    public bool IsTagsDetailVisible => SelectedSettingsSection == SettingsSection.Tags;
    public bool IsHomeLocationDetailVisible => SelectedSettingsSection == SettingsSection.HomeLocation;
    public bool IsAutoTagsDetailVisible => SelectedSettingsSection == SettingsSection.AutoTags;
    public bool IsPhotoExportDetailVisible => SelectedSettingsSection == SettingsSection.PhotoExport;
    public bool IsPreviewCacheDetailVisible => SelectedSettingsSection == SettingsSection.PreviewCache;
    public bool IsResetDetailVisible => SelectedSettingsSection == SettingsSection.Reset;
    public bool IsAppInfoDetailVisible => SelectedSettingsSection == SettingsSection.AppInfo;

    [ObservableProperty]
    private string homeAddress = string.Empty;

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

    [ObservableProperty]
    private string autoTagStateText = "상태 확인 전";

    [ObservableProperty]
    private InfoBarSeverity autoTagInfoSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string autoTagSummary = "자동 태그 상태를 확인해 주세요.";

    [ObservableProperty]
    private string autoTagQuotaWaitingText = string.Empty;

    [ObservableProperty]
    private string autoTagWorkSummary = string.Empty;

    [ObservableProperty]
    private bool hasAutoTagFailures;

    [ObservableProperty]
    private bool hasAutoTagQuotaWaiting;

    [ObservableProperty]
    private int autoTagMonthlyUsage;

    [ObservableProperty]
    private int autoTagMonthlyLimit;

    [ObservableProperty]
    private int autoTagMonthlyLimitForProgress = 1;

    [ObservableProperty]
    private string autoTagMonthlyUsageText = "이번 달 분석량을 확인하고 있습니다.";

    [ObservableProperty]
    private int autoTagWaitingCount;

    [ObservableProperty]
    private int autoTagProcessingCount;

    [ObservableProperty]
    private int autoTagFailedCount;

    [ObservableProperty]
    private int autoTagTodayCompletedCount;

    [ObservableProperty]
    private ObservableCollection<AutoTagFailedItemViewDto> autoTagFailedItems = [];

    [ObservableProperty]
    private int exportTotal;

    [ObservableProperty]
    private int exportCompleted;

    [ObservableProperty]
    private int exportFailed;

    [ObservableProperty]
    private string exportCurrentFile = string.Empty;

    [ObservableProperty]
    private string exportSummary = string.Empty;

    [ObservableProperty]
    private int exportProgressMaximum = 1;

    [ObservableProperty]
    private string exportProgressText = string.Empty;

    [ObservableProperty]
    private bool hasExportActivity;

    [ObservableProperty]
    private bool hasExportResult;

    [ObservableProperty]
    private int exportSucceededCount;

    [ObservableProperty]
    private int exportMetadataPartialCount;

    [ObservableProperty]
    private int exportCopyFailedCount;

    public XamlRoot? HostXamlRoot { get; set; }

    public event EventHandler? ResetCompleted;

    public SettingsViewModel(
        HomeLocationService homeLocationService,
        MemoryKeeperOperationsService operationsService,
        PhotoExportService photoExportService,
        ILocalPreviewCacheService previewCacheService,
        IFolderPickerService folderPickerService,
        StorageManagementViewModel storageSettings,
        INavigationService navigation,
        ILogger<SettingsViewModel> logger)
    {
        _homeLocationService = homeLocationService;
        _operationsService = operationsService;
        _photoExportService = photoExportService;
        _previewCacheService = previewCacheService;
        _folderPickerService = folderPickerService;
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

            var home = await _homeLocationService.GetAsync();
            ApplyHome(home);

            var target = string.IsNullOrWhiteSpace(section) ? "overview" : section;
            ShowSection(target);
            if (target != "tags" && target != "logs")
            {
                try
                {
                    await LoadAutoTagCoreAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load automatic tag status for settings dashboard.");
                    AutoTagStateText = "자동 태그 점검이 필요합니다.";
                    AutoTagInfoSeverity = InfoBarSeverity.Warning;
                    AutoTagSummary = "자동 태그 상태를 확인하지 못했습니다. 잠시 후 다시 확인해 주세요.";
                    AutoTagQuotaWaitingText = string.Empty;
                    AutoTagWorkSummary = string.Empty;
                    HasAutoTagQuotaWaiting = false;
                }
            }
            StatusMessage = "설정을 확인하세요.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings.");
            ShowError(ex is ApiException apiException
                ? ApiErrorClassifier.ToUserMessage(apiException)
                : "설정을 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.");
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
    private void ShowPhotoImport() => ShowSection("photo-management");

    [RelayCommand]
    private void ShowMetadata() => ShowSection("photo-management");

    [RelayCommand]
    private void ShowTags()
    {
        ShowSection("tag-management");
    }

    [RelayCommand]
    private void OpenTagManagement() => ShowSection("tag-management");

    [RelayCommand]
    private async Task SelectSettingsSectionAsync(string? section)
    {
        ShowSection(section ?? "photo-management");
        if (SelectedSettingsSection == SettingsSection.AutoTags)
        {
            await RunBusyAsync(
                LoadAutoTagCoreAsync,
                "자동 태그 상태를 확인하지 못했습니다. 잠시 후 다시 시도해 주세요.");
        }
    }

    [RelayCommand]
    private async Task ShowAiAsync()
    {
        ShowSection("ai");
        await RunBusyAsync(LoadAutoTagCoreAsync, "자동 태그 상태를 확인하지 못했습니다. 잠시 후 다시 시도해 주세요.");
    }

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
    private void GoBack()
    {
        if (IsLogsVisible)
        {
            IsLogsVisible = false;
            IsSettingsDashboardVisible = true;
            ShowSection(ToSectionKey(SelectedSettingsSection));
            return;
        }

        ShowSection(ToSectionKey(SelectedSettingsSection));
    }

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
        }, "집 위치를 저장하지 못했습니다. 잠시 후 다시 시도해 주세요.");
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
        }, "집 위치를 저장하지 못했습니다. 잠시 후 다시 시도해 주세요.");
    }

    [RelayCommand]
    private async Task RefreshAutoTagStatusAsync() =>
        await RunBusyAsync(LoadAutoTagCoreAsync, "자동 태그 상태를 확인하지 못했습니다. 잠시 후 다시 시도해 주세요.");

    [RelayCommand]
    private async Task RetryFailedAutoTagsAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _operationsService.RetryFailedAutoTagsAsync(limit: 500);
            await LoadAutoTagCoreAsync();
            ShowSuccess($"실패한 사진 {result.RequeuedCount:N0}장을 다시 분석하도록 준비했습니다.");
        }, "자동 태그 재시도를 준비하지 못했습니다. 잠시 후 다시 시도해 주세요.");
    }

    [RelayCommand]
    private async Task RetryAutoTagJobAsync(AutoTagFailedItemViewDto? item)
    {
        if (item is null || !item.Retryable)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _operationsService.RetryAutoTagJobAsync(item.JobId);
            await LoadAutoTagCoreAsync();
            ShowSuccess(result.RequeuedCount > 0
                ? "선택한 사진을 다시 분석하도록 준비했습니다."
                : "선택한 사진의 현재 상태를 다시 확인했습니다.");
        }, "선택한 사진의 자동 태그를 다시 준비하지 못했습니다.");
    }

    [RelayCommand]
    private async Task ExportPhotosAsync()
    {
        var destination = await _folderPickerService.PickFolderAsync("MemoryKeeper 사진 내보내기 폴더 선택");
        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        ExportTotal = 0;
        ExportCompleted = 0;
        ExportFailed = 0;
        ExportCurrentFile = string.Empty;
        ExportSummary = "내보내기를 준비하고 있습니다.";
        ExportProgressMaximum = 1;
        ExportProgressText = string.Empty;
        ExportSucceededCount = 0;
        ExportMetadataPartialCount = 0;
        ExportCopyFailedCount = 0;
        HasExportActivity = true;
        HasExportResult = false;
        var progress = new Progress<PhotoExportProgressDto>(value =>
        {
            ExportTotal = value.Total;
            ExportCompleted = value.Completed;
            ExportFailed = value.Failed;
            ExportCurrentFile = value.CurrentFileName;
            ExportProgressMaximum = Math.Max(1, value.Total);
            ExportProgressText = value.Total <= 0
                ? "내보낼 사진을 확인하고 있습니다."
                : $"{value.Completed:N0} / {value.Total:N0}";
        });

        await RunBusyAsync(async () =>
        {
            var result = await _photoExportService.ExportAsync(destination, progress);
            ExportSummary = $"{result.ExportedCount:N0}장 내보냄 · 메타데이터 일부 미기록 {result.MetadataPartialCount:N0}장 · 복사 실패 {result.CopyFailedCount:N0}장";
            ExportSucceededCount = result.ExportedCount;
            ExportMetadataPartialCount = result.MetadataPartialCount;
            ExportCopyFailedCount = result.CopyFailedCount;
            ExportProgressMaximum = Math.Max(1, result.TotalCount);
            ExportProgressText = $"{result.TotalCount:N0} / {result.TotalCount:N0}";
            HasExportResult = true;
            if (result.TotalCount > 0 && result.ExportedCount == 0)
            {
                ShowError("사진을 내보내지 못했습니다. 저장 위치와 네트워크 연결을 확인한 뒤 다시 시도해 주세요.");
            }
            else
            {
                ShowSuccess("사진 내보내기가 완료되었습니다.");
            }
        }, "사진을 내보내지 못했습니다. 잠시 후 다시 시도해 주세요.");
    }

    [RelayCommand]
    private async Task ClearThumbnailCacheAsync()
    {
        if (!await UserFeedback.ConfirmAsync(
                HostXamlRoot,
                "임시 미리보기 정리",
                "이 PC에 저장된 임시 미리보기 데이터만 정리합니다. 원본 사진과 NAS의 사진 데이터는 유지됩니다.",
                primaryText: "정리"))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _previewCacheService.ClearAsync();
            if (result.Succeeded)
            {
                ShowSuccess(result.Message);
            }
            else
            {
                ShowError(result.Message);
            }
        }, "임시 미리보기 데이터를 정리하지 못했습니다.");
    }

    [RelayCommand]
    private async Task RestartMemoryKeeperAsync()
    {
        MemoryKeeperResetPreviewDto? preview = null;
        await RunBusyAsync(async () => preview = await _operationsService.PreviewResetAsync(),
            "처음부터 다시 구성할 항목을 확인하지 못했습니다.");
        if (preview is null)
        {
            return;
        }

        if (preview.ResetBlocked)
        {
            ShowError(
                $"진행 중인 사진 등록 작업 {preview.ActiveUploadJobCount:N0}건" +
                (preview.ProcessingVisionJobCount > 0
                    ? $"과 사진 분석 작업 {preview.ProcessingVisionJobCount:N0}건"
                    : string.Empty) +
                "이 있어 초기화할 수 없습니다. 작업이 완료된 뒤 다시 시도해 주세요.");
            return;
        }

        var previewContent = new StackPanel { Spacing = 12, MaxWidth = 460 };
        previewContent.Children.Add(new TextBlock
        {
            Text = "현재 MemoryKeeper의 정리 결과를 초기화하고 원본 사진을 다시 등록할 수 있습니다.",
            TextWrapping = TextWrapping.Wrap,
        });
        previewContent.Children.Add(new TextBlock
        {
            Text = "초기화되는 정보",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0),
        });
        foreach (var line in new[]
                 {
                     $"• 등록 사진 {preview.MemorykeeperFileCount:N0}장",
                     $"• 장소 {preview.PlaceCount:N0}개",
                     $"• 사용자 태그 {preview.UserTagCount:N0}개",
                     $"• 즐겨찾기 {preview.FavoriteCount:N0}개",
                     $"• 메모 {preview.MemoCount:N0}개",
                 })
        {
            previewContent.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
        }

        previewContent.Children.Add(new TextBlock
        {
            Text = "보존되는 정보",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0),
        });
        foreach (var line in new[]
                 {
                     "• 원본 사진",
                     "• AstroJournal 데이터",
                     "• 재사용 가능한 사진 분석 정보",
                 })
        {
            previewContent.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
        }

        previewContent.Children.Add(new TextBlock
        {
            Text = "원본 사진은 삭제되지 않습니다.",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["MkBrushSuccess"],
            Margin = new Thickness(0, 4, 0, 0),
        });
        var previewDialog = new ContentDialog
        {
            XamlRoot = HostXamlRoot,
            Title = "MemoryKeeper를 처음부터 다시 구성할까요?",
            Content = previewContent,
            PrimaryButtonText = "계속",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await previewDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var confirmation = new TextBox
        {
            PlaceholderText = "초기화",
            MaxWidth = 320,
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "계속하려면 ‘초기화’ 또는 ‘다시 시작’을 입력하세요.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(confirmation);
        var dialog = new ContentDialog
        {
            XamlRoot = HostXamlRoot,
            Title = "마지막 확인",
            Content = content,
            PrimaryButtonText = "처음부터 다시 구성",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
            PrimaryButtonStyle = (Style)Microsoft.UI.Xaml.Application.Current.Resources["MkDangerButtonStyle"],
        };
        confirmation.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = MemoryKeeperOperationsService.IsUserConfirmationValid(confirmation.Text);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _operationsService.ExecuteResetAsync(confirmation.Text);
            if (!result.ResetCompleted)
            {
                throw new InvalidOperationException("처음부터 다시 구성을 완료하지 못했습니다.");
            }

            ShowSuccess("MemoryKeeper가 초기화되었습니다. 원본 사진은 그대로 보존되어 있습니다. 사진 관리에서 다시 등록할 수 있습니다.");
            ResetCompleted?.Invoke(this, EventArgs.Empty);
        }, "MemoryKeeper를 처음부터 다시 구성하지 못했습니다. 잠시 후 다시 시도해 주세요.");
    }

    private async Task LoadAutoTagCoreAsync()
    {
        var status = await _operationsService.GetAutoTagStatusAsync();
        AutoTagStateText = status.State switch
        {
            AutoTagUserState.Normal => "정상 작동 중",
            AutoTagUserState.MonthlyLimitReached => "이번 달 무료 분석량을 모두 사용했습니다.",
            _ => "자동 태그 점검이 필요합니다.",
        };
        AutoTagInfoSeverity = status.State switch
        {
            AutoTagUserState.Normal => InfoBarSeverity.Success,
            AutoTagUserState.MonthlyLimitReached => InfoBarSeverity.Informational,
            _ => InfoBarSeverity.Warning,
        };
        AutoTagSummary = status.Summary;
        AutoTagQuotaWaitingText = status.QuotaWaitingText;
        HasAutoTagQuotaWaiting = status.Status.QuotaWaitingCount > 0;
        AutoTagMonthlyUsage = status.Status.MonthlyUsage;
        AutoTagMonthlyLimit = status.Status.MonthlyLimit;
        AutoTagMonthlyLimitForProgress = Math.Max(1, status.Status.MonthlyLimit);
        AutoTagMonthlyUsageText = status.Status.MonthlyLimit > 0
            ? $"{status.Status.MonthlyUsage:N0} / {status.Status.MonthlyLimit:N0}장"
            : $"{status.Status.MonthlyUsage:N0}장";
        AutoTagWaitingCount = status.Status.WaitingCount;
        AutoTagProcessingCount = status.Status.ProcessingCount;
        AutoTagFailedCount = status.Status.FailedCount;
        AutoTagTodayCompletedCount = status.Status.TodayCompletedCount;
        AutoTagWorkSummary =
            $"대기 {status.Status.WaitingCount:N0} · 처리 중 {status.Status.ProcessingCount:N0} · 실패 {status.Status.FailedCount:N0} · 오늘 완료 {status.Status.TodayCompletedCount:N0}";
        var failed = status.Status.FailedCount > 0
            ? await _operationsService.GetFailedAutoTagsAsync()
            : [];
        AutoTagFailedItems = new ObservableCollection<AutoTagFailedItemViewDto>(failed);
        HasAutoTagFailures = AutoTagFailedItems.Count > 0;
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
            ShowError("주소 검색에 연결할 수 없습니다. 잠시 후 다시 시도해 주세요.");
        }
    }

    private void ShowSection(string section)
    {
        var normalized = string.IsNullOrWhiteSpace(section) ? "photo-management" : section;
        SelectedSettingsSection = normalized switch
        {
            "overview" or "storage" or "photo" or "photo-management" or "original-photos"
                or "photo-import" or "metadata" or "import" => SettingsSection.PhotoManagement,
            "pending" or "pending-memories" => SettingsSection.PendingMemories,
            "place" or "places" => SettingsSection.Places,
            "tags" or "tag-management" or "tags-detail" => SettingsSection.Tags,
            "home" or "home-location" => SettingsSection.HomeLocation,
            "ai" or "auto-tags" => SettingsSection.AutoTags,
            "maintenance" or "export" or "photo-export" => SettingsSection.PhotoExport,
            "cache" or "preview-cache" => SettingsSection.PreviewCache,
            "reset" => SettingsSection.Reset,
            "info" or "app-info" => SettingsSection.AppInfo,
            _ => SettingsSection.PhotoManagement,
        };

        ActiveSection = normalized == "logs" ? "logs" : ToSectionKey(SelectedSettingsSection);
        IsOverviewVisible = false;
        IsStoragePanelVisible = IsPhotoManagementDetailVisible;
        IsHomePanelVisible = IsHomeLocationDetailVisible;
        IsGeneralVisible = IsStoragePanelVisible || IsHomePanelVisible;
        IsPhotoVisible = IsPhotoManagementDetailVisible;
        IsAiVisible = IsAutoTagsDetailVisible;
        IsMaintenanceVisible = IsPhotoExportDetailVisible || IsPreviewCacheDetailVisible || IsResetDetailVisible;
        IsInfoVisible = IsAppInfoDetailVisible || normalized == "logs";
        IsLogsVisible = normalized == "logs";
        IsSettingsDashboardVisible = !IsLogsVisible;
        BreadcrumbTitle = SelectedSettingsSection switch
        {
            SettingsSection.PhotoManagement => "설정 › 사진 관리",
            SettingsSection.PendingMemories => "설정 › 미완성 추억",
            SettingsSection.Places => "설정 › 장소 관리",
            SettingsSection.Tags => "설정 › 태그 관리",
            SettingsSection.HomeLocation => "설정 › 집 위치",
            SettingsSection.AutoTags => "설정 › 자동 태그",
            SettingsSection.PhotoExport => "설정 › 사진 내보내기",
            SettingsSection.PreviewCache => "설정 › 미리보기 캐시",
            SettingsSection.Reset => "설정 › 처음부터 다시 구성",
            SettingsSection.AppInfo => "설정 › 앱 정보",
            _ => "설정",
        };

        // Keep shell back-stack section in sync so child pages return here.
        if (_navigation.Current is { Tag: "settings" } current)
        {
            _navigation.ReplaceCurrent(current with { SettingsSection = ActiveSection });
        }
        else if (_navigation.Current is null)
        {
            _navigation.ReplaceCurrent(NavigationEntry.TopLevel("settings", "설정", ActiveSection));
        }
    }

    partial void OnSelectedSettingsSectionChanged(SettingsSection value)
    {
        OnPropertyChanged(nameof(IsPhotoManagementDetailVisible));
        OnPropertyChanged(nameof(IsPendingMemoriesDetailVisible));
        OnPropertyChanged(nameof(IsPlacesDetailVisible));
        OnPropertyChanged(nameof(IsTagsDetailVisible));
        OnPropertyChanged(nameof(IsHomeLocationDetailVisible));
        OnPropertyChanged(nameof(IsAutoTagsDetailVisible));
        OnPropertyChanged(nameof(IsPhotoExportDetailVisible));
        OnPropertyChanged(nameof(IsPreviewCacheDetailVisible));
        OnPropertyChanged(nameof(IsResetDetailVisible));
        OnPropertyChanged(nameof(IsAppInfoDetailVisible));
    }

    private static string ToSectionKey(SettingsSection section) => section switch
    {
        SettingsSection.PhotoManagement => "photo-management",
        SettingsSection.PendingMemories => "pending-memories",
        SettingsSection.Places => "places",
        SettingsSection.Tags => "tag-management",
        SettingsSection.HomeLocation => "home-location",
        SettingsSection.AutoTags => "auto-tags",
        SettingsSection.PhotoExport => "photo-export",
        SettingsSection.PreviewCache => "preview-cache",
        SettingsSection.Reset => "reset",
        SettingsSection.AppInfo => "app-info",
        _ => "photo-management",
    };

    private void ApplyHome(HomeLocationDto home)
    {
        _suppressHomeAddressSuggest = true;
        try
        {
            HomeAddress = home.Address;
            HomePlaceId = home.PlaceId;
            HomeResolvedSummary = home.IsConfigured
                ? (string.IsNullOrWhiteSpace(home.Address) ? "집 위치가 설정되었습니다." : home.Address)
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

    private async Task RunBusyAsync(Func<Task> action, string? userErrorMessage = null)
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
            ShowError(userErrorMessage
                      ?? (ex is ApiException apiException
                          ? ApiErrorClassifier.ToUserMessage(apiException)
                          : ex is ArgumentException or InvalidOperationException
                              ? ex.Message
                              : "요청을 처리하지 못했습니다. 잠시 후 다시 시도해 주세요."));
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
