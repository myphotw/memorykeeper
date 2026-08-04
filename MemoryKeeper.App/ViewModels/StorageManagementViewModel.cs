using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Services;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.App.ViewModels;

public partial class StorageManagementViewModel : ObservableObject
{
    private readonly StorageUiOperations _storageUiOperations;
    private readonly ILogger<StorageManagementViewModel> _logger;

    private Guid? _storageId;

    [ObservableProperty]
    private string photoRootPath = string.Empty;

    [ObservableProperty]
    private bool hasCheckedConnection;

    [ObservableProperty]
    private bool folderExists;

    [ObservableProperty]
    private bool isReadable;

    [ObservableProperty]
    private bool isWritable;

    [ObservableProperty]
    private string statusMessage = "MemoryKeeper 저장소를 확인하세요.";

    [ObservableProperty]
    private bool isBusy;

    public string ReadableStatusText =>
        !HasCheckedConnection ? string.Empty : IsReadable ? "✔ 읽기 가능" : "❌ 읽기 불가";

    public string WritableStatusText =>
        !HasCheckedConnection ? string.Empty : IsWritable ? "✔ 쓰기 가능" : "❌ 쓰기 불가";

    public bool ShowConnectionError => HasCheckedConnection && !FolderExists;

    public bool ShowConnectionDetails => HasCheckedConnection && FolderExists;

    public StorageManagementViewModel(
        StorageUiOperations storageUiOperations,
        ILogger<StorageManagementViewModel> logger)
    {
        _storageUiOperations = storageUiOperations;
        _logger = logger;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var state = await _storageUiOperations.LoadAsync();
            ApplyState(state);
        });
    }

    [RelayCommand]
    private async Task ChangeFolderAsync()
    {
        await RunBusyAsync(async () =>
        {
            _logger.LogInformation(
                "Folder change requested. CurrentRoot={CurrentRoot} StorageId={StorageId}",
                PhotoRootPath,
                _storageId);

            var state = await _storageUiOperations.PickAndChangePhotoRootAsync(_storageId);
            if (state is null)
            {
                return;
            }

            ApplyState(state);

            var reloaded = await _storageUiOperations.LoadAsync();
            ApplyState(reloaded);
            _logger.LogInformation(
                "Folder change completed. SavedRoot={SavedRoot} Status={Status}",
                PhotoRootPath,
                StatusMessage);
        });
    }

    [RelayCommand]
    private Task CheckConnectionAsync()
    {
        return RunBusyAsync(async () =>
        {
            var state = await _storageUiOperations.CheckConnectionAsync(_storageId, PhotoRootPath);
            ApplyState(state);
        });
    }

    private void ApplyState(StorageUiState state)
    {
        _storageId = state.StorageId;
        PhotoRootPath = state.PhotoRootPath;
        HasCheckedConnection = state.HasCheckedConnection;
        FolderExists = state.Connection.Exists;
        IsReadable = state.Connection.IsReadable;
        IsWritable = state.Connection.IsWritable;
        StatusMessage = state.StatusMessage;
        NotifyStatusTexts();
    }

    private void NotifyStatusTexts()
    {
        OnPropertyChanged(nameof(ReadableStatusText));
        OnPropertyChanged(nameof(WritableStatusText));
        OnPropertyChanged(nameof(ShowConnectionError));
        OnPropertyChanged(nameof(ShowConnectionDetails));
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
            _logger.LogError(ex, "MemoryKeeper storage UI operation failed.");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
