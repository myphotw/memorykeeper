using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace MemoryKeeper.App.ViewModels;

/// <summary>
/// Import UI + progress (Phase 3B). Upload acceptance and analysis monitoring are separate.
/// </summary>
public partial class ImportViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFolderPickerService _folderPickerService;
    private readonly ILogger<ImportViewModel> _logger;
    private CancellationTokenSource? _importCancellation;
    private IReadOnlyList<MediaImportItemResult> _lastItems = [];
    private Guid? _lastStorageId;

    [ObservableProperty]
    private string memoryKeeperStoragePath = string.Empty;

    [ObservableProperty]
    private string sourceFolderPath = string.Empty;

    [ObservableProperty]
    private string statusMessage = "등록할 폴더를 선택한 뒤 사진 등록을 실행하세요.";

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
    private int pendingCount;

    [ObservableProperty]
    private int uploadingCount;

    [ObservableProperty]
    private int waitingCount;

    [ObservableProperty]
    private int processingCount;

    [ObservableProperty]
    private int completedCount;

    [ObservableProperty]
    private int cancelledCount;

    [ObservableProperty]
    private int uploadFinishedCount;

    [ObservableProperty]
    private int uploadedCount;

    [ObservableProperty]
    private int analysisFinishedCount;

    [ObservableProperty]
    private string currentFileName = string.Empty;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private double uploadProgressValue;

    [ObservableProperty]
    private double analysisProgressValue;

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

    [ObservableProperty]
    private string failureFileName = string.Empty;

    [ObservableProperty]
    private string failureCategory = string.Empty;

    [ObservableProperty]
    private bool hasFailureDetails;

    [ObservableProperty]
    private bool hasExistingSession;

    [ObservableProperty]
    private bool isStalled;

    [ObservableProperty]
    private bool hasPersistenceWarning;

    [ObservableProperty]
    private string lastStatusCheckedText = string.Empty;

    [ObservableProperty]
    private string progressSummary = string.Empty;

    /// <summary>Retry prepared after FAILED.</summary>
    [ObservableProperty]
    private bool isRetryReady;

    public string TotalCountText => $"전체 {TotalCount}";

    public string ProcessedCountText => $"처리 {ProcessedCount}";

    public string RegisteredCountText => $"등록 {ImportedCount}";

    public string DuplicateCountText => $"중복 {DuplicateCount}";

    public string FailedCountText => $"실패 {FailedCount}";

    public string WaitingCountText => $"대기 {WaitingCount}";

    public string ProcessingCountText => $"처리 중 {ProcessingCount}";

    public string UploadingCountText => $"업로드 {UploadingCount}";

    public string ProgressCountText => TotalCount <= 0 ? string.Empty : $"{ProcessedCount} / {TotalCount}";

    public string UploadProgressText =>
        TotalCount <= 0 ? string.Empty : $"전송 {UploadFinishedCount} / {TotalCount}";

    public string AnalysisProgressText =>
        TotalCount <= 0 ? string.Empty : $"분석 {AnalysisFinishedCount} / {TotalCount}";

    public string UploadCompletionMessage =>
        TotalCount > 0 && UploadFinishedCount >= TotalCount
            ? "PC 전송 완료 — 앱/PC를 종료해도 NAS 처리는 계속됩니다."
            : "PC에서 NAS로 사진을 전송하고 있습니다.";

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
            var mediaImportService = scope.ServiceProvider.GetRequiredService<MediaImportService>();
            var storage = await ResolveImportStorageAsync(storageService);

            MemoryKeeperStoragePath = storage?.PhotoRoot ?? string.Empty;

            var resumeProgress = new Progress<ImportProgressDto>(ApplyProgress);
            var resumed = await mediaImportService.ResumePersistedJobsAsync(resumeProgress);
            HasExistingSession = resumed > 0;
            StatusMessage = storage is null
                ? "MemoryKeeper 저장소가 설정되지 않았습니다. 설정에서 폴더를 지정하세요."
                : resumed > 0
                    ? $"기존 사진 등록 작업 {resumed:N0}건을 발견했습니다. NAS 상태를 확인하고 있습니다."
                    : "사진 등록 준비가 완료되었습니다.";
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
            _lastStorageId = storage.Id;

            _importCancellation = new CancellationTokenSource();
            var progress = new Progress<ImportProgressDto>(ApplyProgress);

            StatusMessage = "Backend Upload 실행 중 (병렬 전송 + 분석 모니터)...";
            var result = await mediaImportService.ImportAsync(
                new MediaImportRequest
                {
                    SourceFolderPath = SourceFolderPath,
                    StorageId = storage.Id
                },
                progress,
                _importCancellation.Token);

            ApplyImportResult(result);
        });
    }

    [RelayCommand(CanExecute = nameof(CanRetryImport))]
    private async Task RetryImportAsync()
    {
        var failed = _lastItems.Where(i => i.Status == MediaStatus.Failed).ToList();
        if (failed.Count == 0 || _lastStorageId is null)
        {
            StatusMessage = "재시도할 실패 항목이 없습니다.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var mediaImportService = scope.ServiceProvider.GetRequiredService<MediaImportService>();
            _importCancellation = new CancellationTokenSource();
            var progress = new Progress<ImportProgressDto>(ApplyProgress);
            StatusMessage = "실패 항목 재시도 중...";

            var result = await mediaImportService.RetryFailedAsync(
                _lastStorageId.Value,
                SourceFolderPath,
                failed,
                progress,
                _importCancellation.Token);

            ApplyImportResult(result);
        });
    }

    private bool CanRetryImport() => IsRetryReady && !IsBusy;

    [RelayCommand]
    private void CancelImport()
    {
        _importCancellation?.Cancel();
        StatusMessage = $"추가 파일 전송을 중단합니다. 이미 NAS에 접수된 {UploadedCount:N0}건은 서버에서 계속 처리됩니다.";
    }

    private void ApplyImportResult(MediaImportResult result)
    {
        _lastItems = result.Items;
        _lastStorageId = result.StorageId;

        if (result.FailedCount > 0)
        {
            var failed = result.Items.LastOrDefault(item =>
                item.Status == MediaStatus.Failed && !string.IsNullOrWhiteSpace(item.ErrorMessage));
            FailureMessage = failed?.ErrorMessage
                ?? $"사진 등록 실패 {result.FailedCount}건.";
            FailureFileName = failed?.FileName ?? string.Empty;
            FailureCategory = failed?.ErrorCategory ?? "확인 필요";
            HasFailureDetails = true;
            LastJobId = failed?.ContentHash;
            IsRetryReady = true;
            CurrentStage = UploadJobStatusDto.Failed;
            BackendStatus = UploadJobStatusDto.Failed;
            StatusMessage =
                $"완료(부분). 등록 {result.ImportedCount}, 중복 {result.DuplicateCount}, 실패 {result.FailedCount}";
            _logger.LogWarning(
                "[Photo Register] Import finished with failures. Failed={Failed}, JobId={JobId}",
                result.FailedCount,
                LastJobId);
            _ = UserFeedback.ShowInfoAsync(
                HostXamlRoot,
                "사진 등록 부분 실패",
                FailureMessage);
            return;
        }

        IsRetryReady = false;
        FailureMessage = null;
        HasFailureDetails = false;
        CurrentStage = UploadJobStatusDto.Completed;
        BackendStatus = UploadJobStatusDto.Completed;
        StatusMessage =
            $"사진 등록 완료. 전체 {result.ScannedCount}, 등록 {result.ImportedCount}, 중복 {result.DuplicateCount}, 실패 {result.FailedCount}";

        _logger.LogInformation(
            "[Photo Register] COMPLETED — Gallery Reload via catalog invalidation.");

        _ = UserFeedback.ShowInfoAsync(
            HostXamlRoot,
            "사진 등록 완료",
            "사진 등록이 완료되었습니다. 갤러리가 갱신됩니다.");
        ImportCompletedNavigateHome?.Invoke(this, EventArgs.Empty);
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
        PendingCount = progress.PendingCount;
        UploadingCount = progress.UploadingCount;
        WaitingCount = progress.WaitingCount;
        ProcessingCount = progress.ProcessingCount;
        CompletedCount = progress.CompletedCount;
        CancelledCount = progress.CancelledCount;
        UploadFinishedCount = progress.UploadFinishedCount;
        UploadedCount = progress.UploadedCount;
        AnalysisFinishedCount = progress.AnalysisFinishedCount;
        CurrentFileName = progress.CurrentFileName ?? string.Empty;
        BackendStatus = progress.BackendStatus ?? string.Empty;
        BackendProgress = progress.BackendProgress ?? 0;
        CurrentPlugin = progress.CurrentPlugin ?? string.Empty;
        ProgressSummary = progress.StatusSummary ?? string.Empty;
        HasExistingSession = progress.IsResumedSession;
        IsStalled = progress.IsStalled;
        HasPersistenceWarning = progress.HasPersistenceWarning;
        LastStatusCheckedText = progress.LastStatusCheckedAt is null
            ? string.Empty
            : $"마지막 확인 {progress.LastStatusCheckedAt.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss}";
        UploadProgressValue = progress.UploadProgressRatio;
        AnalysisProgressValue = progress.AnalysisProgressRatio;
        if (!string.IsNullOrWhiteSpace(progress.JobId))
        {
            LastJobId = progress.JobId;
        }

        if (!string.IsNullOrWhiteSpace(progress.LastError))
        {
            FailureMessage = progress.LastError;
            FailureFileName = progress.LastFailureFileName ?? CurrentFileName;
            FailureCategory = progress.LastErrorCategory ?? "확인 필요";
            HasFailureDetails = true;
            IsRetryReady = true;
        }

        CurrentStage = string.IsNullOrWhiteSpace(progress.CurrentStage)
            ? (string.IsNullOrWhiteSpace(BackendStatus) ? "처리 중..." : BackendStatus)
            : progress.CurrentStage;
        ProgressValue = progress.ProgressRatio;
        StatusMessage = string.IsNullOrWhiteSpace(ProgressSummary)
            ? (string.IsNullOrWhiteSpace(BackendProgressText)
                ? (string.IsNullOrWhiteSpace(CurrentFileName)
                    ? CurrentStage
                    : $"{CurrentStage} · {CurrentFileName}")
                : $"{BackendProgressText} · {CurrentFileName}")
            : ProgressSummary;
        NotifyProgressTexts();
    }

    private void ResetProgress()
    {
        TotalCount = 0;
        ProcessedCount = 0;
        ImportedCount = 0;
        DuplicateCount = 0;
        FailedCount = 0;
        PendingCount = 0;
        UploadingCount = 0;
        WaitingCount = 0;
        ProcessingCount = 0;
        CompletedCount = 0;
        CancelledCount = 0;
        UploadFinishedCount = 0;
        UploadedCount = 0;
        AnalysisFinishedCount = 0;
        CurrentFileName = string.Empty;
        CurrentStage = string.Empty;
        ProgressValue = 0;
        UploadProgressValue = 0;
        AnalysisProgressValue = 0;
        ProgressSummary = string.Empty;
        BackendStatus = string.Empty;
        BackendProgress = 0;
        CurrentPlugin = string.Empty;
        LastJobId = null;
        FailureMessage = null;
        FailureFileName = string.Empty;
        FailureCategory = string.Empty;
        HasFailureDetails = false;
        IsStalled = false;
        HasPersistenceWarning = false;
        LastStatusCheckedText = string.Empty;
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
        OnPropertyChanged(nameof(WaitingCountText));
        OnPropertyChanged(nameof(ProcessingCountText));
        OnPropertyChanged(nameof(UploadingCountText));
        OnPropertyChanged(nameof(ProgressCountText));
        OnPropertyChanged(nameof(UploadProgressText));
        OnPropertyChanged(nameof(AnalysisProgressText));
        OnPropertyChanged(nameof(UploadCompletionMessage));
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
            StatusMessage = $"추가 파일 전송을 중단했습니다. 이미 NAS에 접수된 {UploadedCount:N0}건은 서버에서 계속 처리됩니다.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Photo register UI operation failed.");
            StatusMessage = "사진 등록 중 오류가 발생했습니다.";
            FailureMessage = ex.Message;
            IsRetryReady = true;
            ErrorDialog.Show(
                ErrorReportSource.Import,
                "Memory Keeper — 사진 등록 오류",
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
