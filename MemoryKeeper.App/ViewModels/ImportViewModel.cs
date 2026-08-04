using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace MemoryKeeper.App.ViewModels;

/// <summary>
/// Import UI + progress (M8). There is no separate ImportProgressViewModel;
/// backend WAITING/PROCESSING/COMPLETED/FAILED progress lives here.
/// </summary>
public partial class ImportViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFolderPickerService _folderPickerService;
    private readonly ILogger<ImportViewModel> _logger;
    private CancellationTokenSource? _importCancellation;

    [ObservableProperty]
    private string memoryKeeperStoragePath = string.Empty;

    [ObservableProperty]
    private string sourceFolderPath = string.Empty;

    [ObservableProperty]
    private string statusMessage = "등록할 폴더를 선택한 뒤 사진등록을 실행하세요.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private int processedCount;

    [ObservableProperty]
    private int importedCount;

    [ObservableProperty]
    private int duplicateCount;

    [ObservableProperty]
    private int failedCount;

    [ObservableProperty]
    private string currentFileName = string.Empty;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private string currentStage = string.Empty;

    [ObservableProperty]
    private string backendStatus = string.Empty;

    [ObservableProperty]
    private int backendProgress;

    [ObservableProperty]
    private string currentPlugin = string.Empty;

    [ObservableProperty]
    private string? lastJobId;

    [ObservableProperty]
    private string? failureMessage;

    /// <summary>Retry prepared after FAILED. Retry UI is not wired yet (M8).</summary>
    [ObservableProperty]
    private bool isRetryReady;

    public string TotalCountText => $"전체 {TotalCount}";

    public string ProcessedCountText => $"처리 {ProcessedCount}";

    public string RegisteredCountText => $"등록 {ImportedCount}";

    public string DuplicateCountText => $"중복 {DuplicateCount}";

    public string FailedCountText => $"실패 {FailedCount}";

    public string ProgressCountText => TotalCount <= 0 ? string.Empty : $"{ProcessedCount} / {TotalCount}";

    public string BackendProgressText =>
        string.IsNullOrWhiteSpace(BackendStatus)
            ? string.Empty
            : string.IsNullOrWhiteSpace(CurrentPlugin)
                ? $"{BackendStatus} · {BackendProgress}%"
                : $"{BackendStatus} · {BackendProgress}% · {CurrentPlugin}";

    public event EventHandler? ImportCompletedNavigateHome;

    public event EventHandler? BackRequested;

    public XamlRoot? HostXamlRoot { get; set; }

    public ImportViewModel(
        IServiceScopeFactory scopeFactory,
        IFolderPickerService folderPickerService,
        ILogger<ImportViewModel> logger)
    {
        _scopeFactory = scopeFactory;
        _folderPickerService = folderPickerService;
        _logger = logger;
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var storageService = scope.ServiceProvider.GetRequiredService<StorageService>();
            var storage = await ResolveImportStorageAsync(storageService);

            MemoryKeeperStoragePath = storage?.PhotoRoot ?? string.Empty;
            StatusMessage = storage is null
                ? "MemoryKeeper 저장소가 설정되지 않았습니다. 설정에서 폴더를 지정하세요."
                : "사진등록 준비가 완료되었습니다.";
        }, markBusy: false);
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        var path = await _folderPickerService.PickFolderAsync("등록할 폴더 선택");
        if (!string.IsNullOrWhiteSpace(path))
        {
            SourceFolderPath = path;
            StatusMessage = $"등록 폴더: {path}";
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceFolderPath))
        {
            StatusMessage = "등록할 폴더를 선택하세요.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var storageService = scope.ServiceProvider.GetRequiredService<StorageService>();
            var mediaImportService = scope.ServiceProvider.GetRequiredService<MediaImportService>();

            var storage = await ResolveImportStorageAsync(storageService);
            if (storage is null)
            {
                StatusMessage = "MemoryKeeper 저장소가 설정되지 않았습니다. 설정에서 폴더를 지정하세요.";
                return;
            }

            MemoryKeeperStoragePath = storage.PhotoRoot;
            ResetProgress();

            _importCancellation = new CancellationTokenSource();
            var progress = new Progress<ImportProgressDto>(ApplyProgress);

            StatusMessage = "Backend Upload 실행 중...";
            var result = await mediaImportService.ImportAsync(
                new MediaImportRequest
                {
                    SourceFolderPath = SourceFolderPath,
                    StorageId = storage.Id
                },
                progress,
                _importCancellation.Token);

            if (result.FailedCount > 0)
            {
                var failed = result.Items.LastOrDefault(item =>
                    !string.IsNullOrWhiteSpace(item.ErrorMessage));
                FailureMessage = failed?.ErrorMessage
                    ?? $"사진등록 실패 {result.FailedCount}건.";
                LastJobId = failed?.ContentHash;
                IsRetryReady = true;
                CurrentStage = UploadJobStatusDto.Failed;
                BackendStatus = UploadJobStatusDto.Failed;
                StatusMessage = FailureMessage;
                _logger.LogWarning(
                    "[Photo Register] Import FAILED. Failed={Failed}, JobId={JobId}, RetryReady={RetryReady}",
                    result.FailedCount,
                    LastJobId,
                    IsRetryReady);
                await UserFeedback.ShowInfoAsync(
                    HostXamlRoot,
                    "사진 등록 실패",
                    FailureMessage);
                return;
            }

            IsRetryReady = false;
            FailureMessage = null;
            CurrentStage = UploadJobStatusDto.Completed;
            BackendStatus = UploadJobStatusDto.Completed;
            StatusMessage =
                $"사진등록 완료. 전체 {result.ScannedCount}, 등록 {result.ImportedCount}, 실패 {result.FailedCount}";

            _logger.LogInformation(
                "[Photo Register] COMPLETED — Gallery Reload via catalog invalidation.");

            await UserFeedback.ShowInfoAsync(
                HostXamlRoot,
                "사진 등록 완료",
                "사진 등록이 완료되었습니다. 갤러리가 갱신됩니다.");
            ImportCompletedNavigateHome?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Retry entry point prepared for FAILED jobs. Not bound to UI yet (M8).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRetryImport))]
    private Task RetryImportAsync()
    {
        StatusMessage = "Retry는 준비되었습니다. UI는 아직 연결되지 않았습니다.";
        return Task.CompletedTask;
    }

    private bool CanRetryImport() => IsRetryReady && !IsBusy;

    [RelayCommand]
    private void CancelImport()
    {
        _importCancellation?.Cancel();
        StatusMessage = "사진등록 취소가 요청되었습니다.";
    }

    private static async Task<StorageDto?> ResolveImportStorageAsync(StorageService storageService)
    {
        var items = await storageService.GetStorageListAsync();
        var active = items.FirstOrDefault(storage => storage.IsActive);
        if (active is not null)
        {
            return active;
        }

        var fallback = items.FirstOrDefault();
        if (fallback is null)
        {
            return null;
        }

        return await storageService.SetActiveStorageAsync(fallback.Id);
    }

    private void ApplyProgress(ImportProgressDto progress)
    {
        TotalCount = progress.TotalCount;
        ProcessedCount = progress.ProcessedCount;
        ImportedCount = progress.ImportedCount;
        DuplicateCount = progress.DuplicateCount;
        FailedCount = progress.FailedCount;
        CurrentFileName = progress.CurrentFileName ?? string.Empty;
        BackendStatus = progress.BackendStatus ?? string.Empty;
        BackendProgress = progress.BackendProgress ?? 0;
        CurrentPlugin = progress.CurrentPlugin ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(progress.JobId))
        {
            LastJobId = progress.JobId;
        }

        if (progress.IsFailed)
        {
            FailureMessage = progress.LastError;
            IsRetryReady = true;
        }

        CurrentStage = string.IsNullOrWhiteSpace(progress.CurrentStage)
            ? (string.IsNullOrWhiteSpace(BackendStatus) ? "처리 중..." : BackendStatus)
            : progress.CurrentStage;
        ProgressValue = progress.ProgressRatio;
        StatusMessage = string.IsNullOrWhiteSpace(BackendProgressText)
            ? (string.IsNullOrWhiteSpace(CurrentFileName)
                ? CurrentStage
                : $"{CurrentStage} · {CurrentFileName} ({ProgressCountText})")
            : string.IsNullOrWhiteSpace(CurrentFileName)
                ? BackendProgressText
                : $"{BackendProgressText} · {CurrentFileName} ({ProgressCountText})";
        NotifyProgressTexts();
    }

    private void ResetProgress()
    {
        TotalCount = 0;
        ProcessedCount = 0;
        ImportedCount = 0;
        DuplicateCount = 0;
        FailedCount = 0;
        CurrentFileName = string.Empty;
        CurrentStage = string.Empty;
        ProgressValue = 0;
        BackendStatus = string.Empty;
        BackendProgress = 0;
        CurrentPlugin = string.Empty;
        LastJobId = null;
        FailureMessage = null;
        IsRetryReady = false;
        NotifyProgressTexts();
    }

    private void NotifyProgressTexts()
    {
        OnPropertyChanged(nameof(TotalCountText));
        OnPropertyChanged(nameof(ProcessedCountText));
        OnPropertyChanged(nameof(RegisteredCountText));
        OnPropertyChanged(nameof(DuplicateCountText));
        OnPropertyChanged(nameof(FailedCountText));
        OnPropertyChanged(nameof(ProgressCountText));
        OnPropertyChanged(nameof(BackendProgressText));
        RetryImportCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRetryReadyChanged(bool value) => RetryImportCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => RetryImportCommand.NotifyCanExecuteChanged();

    private async Task RunBusyAsync(Func<Task> action, bool markBusy = true)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            if (markBusy)
            {
                IsBusy = true;
            }

            await action();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "사진등록이 취소되었습니다.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Photo register UI operation failed.");
            StatusMessage = "사진등록 중 오류가 발생했습니다.";
            FailureMessage = ex.Message;
            IsRetryReady = true;
            ErrorDialog.Show(
                ErrorReportSource.Import,
                "Memory Keeper — 사진등록 오류",
                ex,
                stage: "ImportViewModel.RunBusyAsync");
        }
        finally
        {
            if (markBusy)
            {
                IsBusy = false;
            }

            _importCancellation?.Dispose();
            _importCancellation = null;
        }
    }
}
