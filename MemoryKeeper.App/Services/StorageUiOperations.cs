using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.App.Services;

public sealed class StorageUiState
{
    public Guid? StorageId { get; init; }

    public string PhotoRootPath { get; init; } = string.Empty;

    public bool HasCheckedConnection { get; init; }

    public StorageConnectionResult Connection { get; init; } = new();

    public string StatusMessage { get; init; } = string.Empty;
}

public sealed class StorageUiOperations
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFolderPickerService _folderPickerService;
    private readonly ILogger<StorageUiOperations> _logger;

    public StorageUiOperations(
        IServiceScopeFactory scopeFactory,
        IFolderPickerService folderPickerService,
        ILogger<StorageUiOperations> logger)
    {
        _scopeFactory = scopeFactory;
        _folderPickerService = folderPickerService;
        _logger = logger;
    }

    public async Task<StorageUiState> LoadAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var storageService = scope.ServiceProvider.GetRequiredService<StorageService>();

        var items = await storageService.GetStorageListAsync();
        var active = items.FirstOrDefault(storage => storage.IsActive) ?? items.FirstOrDefault();

        if (active is null)
        {
            _logger.LogInformation("Storage load: no storage configured.");
            return new StorageUiState
            {
                StatusMessage = "MemoryKeeper 저장소가 설정되지 않았습니다. 폴더를 선택하세요."
            };
        }

        _logger.LogInformation(
            "Storage load from DB. StorageId={StorageId} PhotoRoot={PhotoRoot}",
            active.Id,
            active.PhotoRoot);

        var connection = StorageConnectionChecker.Check(active.PhotoRoot, _logger);
        LogConnection("Storage load validate", active.PhotoRoot, connection);

        return BuildState(active.Id, active.PhotoRoot, connection, BuildConnectionMessage(connection));
    }

    public async Task<StorageUiState?> PickAndChangePhotoRootAsync(Guid? storageId)
    {
        var path = await _folderPickerService.PickFolderAsync("MemoryKeeper 저장소 폴더 선택");
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogInformation("Storage folder change cancelled.");
            return null;
        }

        return await ChangePhotoRootAsync(storageId, path);
    }

    public async Task<StorageUiState> ChangePhotoRootAsync(Guid? storageId, string path)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var storageService = scope.ServiceProvider.GetRequiredService<StorageService>();

        var beforeItems = await storageService.GetStorageListAsync();
        var beforeActive = beforeItems.FirstOrDefault(storage => storage.IsActive)
            ?? beforeItems.FirstOrDefault(storage => storageId is null || storage.Id == storageId);
        var currentRoot = beforeActive?.PhotoRoot ?? string.Empty;
        var resolvedStorageId = storageId ?? beforeActive?.Id;

        _logger.LogInformation(
            "PhotoRoot change start. CurrentRoot={CurrentRoot} SelectedRoot={SelectedRoot} StorageId={StorageId}",
            currentRoot,
            path,
            resolvedStorageId);

        Directory.CreateDirectory(path);
        _logger.LogInformation(
            "Directory.CreateDirectory completed. SelectedRoot={SelectedRoot} Exists={Exists}",
            path,
            Directory.Exists(path));

        StorageDto updated;
        if (resolvedStorageId is null)
        {
            _logger.LogInformation("No existing storage. Calling CreateStorageAsync.");
            updated = await storageService.CreateStorageAsync(new CreateStorageRequest
            {
                Name = "MemoryKeeper",
                StorageType = StorageType.Local,
                PhotoRoot = path,
                SetAsActive = true
            });
        }
        else
        {
            _logger.LogInformation(
                "Calling UpdatePhotoRootAsync. StorageId={StorageId} SelectedRoot={SelectedRoot}",
                resolvedStorageId.Value,
                path);
            updated = await storageService.UpdatePhotoRootAsync(resolvedStorageId.Value, path);
        }

        var afterItems = await storageService.GetStorageListAsync();
        var verified = afterItems.FirstOrDefault(storage => storage.Id == updated.Id)
            ?? afterItems.FirstOrDefault(storage => storage.IsActive);

        var savedRoot = verified?.PhotoRoot ?? updated.PhotoRoot;
        _logger.LogInformation(
            "PhotoRoot saved to DB. BeforeRoot={BeforeRoot} SavedRoot={SavedRoot} ValidateRoot={ValidateRoot}",
            currentRoot,
            updated.PhotoRoot,
            savedRoot);

        var connection = StorageConnectionChecker.Check(savedRoot, _logger);
        LogConnection("PhotoRoot change validate", savedRoot, connection);

        return BuildState(
            verified?.Id ?? updated.Id,
            savedRoot,
            connection,
            connection.IsHealthy
                ? "MemoryKeeper 저장소가 정상적으로 연결되었습니다."
                : BuildConnectionMessage(connection));
    }

    public async Task<StorageUiState> CheckConnectionAsync(Guid? storageId, string? photoRootPath)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var storageService = scope.ServiceProvider.GetRequiredService<StorageService>();

        var items = await storageService.GetStorageListAsync();
        var active = items.FirstOrDefault(storage => storage.IsActive) ?? items.FirstOrDefault();
        var validateRoot = !string.IsNullOrWhiteSpace(photoRootPath)
            ? photoRootPath
            : active?.PhotoRoot ?? string.Empty;
        var resolvedStorageId = storageId ?? active?.Id;

        _logger.LogInformation(
            "Manual connection check. DisplayRoot={DisplayRoot} ValidateRoot={ValidateRoot} StorageId={StorageId}",
            photoRootPath,
            validateRoot,
            resolvedStorageId);

        var connection = StorageConnectionChecker.Check(validateRoot, _logger);
        LogConnection("Manual validate", validateRoot, connection);

        return BuildState(
            resolvedStorageId,
            validateRoot,
            connection,
            BuildConnectionMessage(connection));
    }

    public static string BuildConnectionMessage(StorageConnectionResult connection)
    {
        if (!connection.Exists)
        {
            return "❌ 접근할 수 없습니다.";
        }

        if (connection.IsHealthy)
        {
            return "✔ 정상";
        }

        return "❌ 접근할 수 없습니다.";
    }

    private static StorageUiState BuildState(
        Guid? storageId,
        string photoRootPath,
        StorageConnectionResult connection,
        string statusMessage)
    {
        return new StorageUiState
        {
            StorageId = storageId,
            PhotoRootPath = photoRootPath,
            HasCheckedConnection = true,
            Connection = connection,
            StatusMessage = statusMessage
        };
    }

    private void LogConnection(string phase, string validateRoot, StorageConnectionResult connection)
    {
        _logger.LogInformation(
            "{Phase}. ValidateRoot={ValidateRoot} Exists={Exists} CanRead={CanRead} CanWrite={CanWrite}",
            phase,
            validateRoot,
            connection.Exists,
            connection.IsReadable,
            connection.IsWritable);
    }
}
