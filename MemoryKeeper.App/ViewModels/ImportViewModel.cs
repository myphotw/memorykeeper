using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Diagnostics;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace MemoryKeeper.App.ViewModels;

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

    public string TotalCountText => $"전체 {TotalCount}";

    public string ProcessedCountText => $"처리 {ProcessedCount}";

    public string RegisteredCountText => $"등록 {ImportedCount}";

    public string DuplicateCountText => $"중복 {DuplicateCount}";

    public string FailedCountText => $"실패 {FailedCount}";

    public string ProgressCountText => TotalCount <= 0 ? string.Empty : $"{ProcessedCount} / {TotalCount}";

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

            StatusMessage = "사진등록을 실행 중입니다...";
            var result = await mediaImportService.ImportAsync(
                new MediaImportRequest
                {
                    SourceFolderPath = SourceFolderPath,
                    StorageId = storage.Id
                },
                progress,
                _importCancellation.Token);

            StatusMessage =
                $"사진등록 완료. 전체 {result.ScannedCount}, 등록 {result.ImportedCount}, 중복 {result.DuplicateCount}, 실패 {result.FailedCount}";
            CurrentStage = "완료";

            _logger.LogInformation(
                "[Photo Register] Gallery Refresh, Home Refresh, VisitMap Refresh are available after navigating to those screens.");

            await UserFeedback.ShowInfoAsync(
                HostXamlRoot,
                "사진 등록 완료",
                "사진 등록이 완료되었습니다.");
            ImportCompletedNavigateHome?.Invoke(this, EventArgs.Empty);
        });
    }

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
        CurrentStage = string.IsNullOrWhiteSpace(progress.CurrentStage)
            ? (string.IsNullOrWhiteSpace(progress.CurrentFileName) ? "사진 분석 중..." : "처리 중...")
            : progress.CurrentStage;
        ProgressValue = progress.ProgressRatio;
        StatusMessage = string.IsNullOrWhiteSpace(CurrentFileName)
            ? CurrentStage
            : $"{CurrentStage} · {CurrentFileName} ({ProgressCountText})";
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
    }

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
