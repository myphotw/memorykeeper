using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Application.Services;

public sealed class StorageService
{
    private readonly IStorageRepository _storageRepository;
    private readonly IFileAccessService _fileAccessService;
    private readonly ILogger<StorageService> _logger;

    public StorageService(
        IStorageRepository storageRepository,
        IFileAccessService fileAccessService,
        ILogger<StorageService> logger)
    {
        _storageRepository = storageRepository;
        _fileAccessService = fileAccessService;
        _logger = logger;
    }

    public async Task<StorageDto> CreateStorageAsync(
        CreateStorageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateName(request.Name);
        ValidatePhotoRoot(request.PhotoRoot);

        var now = DateTime.UtcNow;
        var storage = new StorageEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            StorageType = request.StorageType,
            PhotoRoot = NormalizePath(request.PhotoRoot),
            IsActive = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _storageRepository.AddAsync(storage, cancellationToken);

        if (request.SetAsActive)
        {
            await SetActiveStorageAsync(storage.Id, cancellationToken);
            storage = await _storageRepository.GetByIdAsync(storage.Id, cancellationToken)
                ?? storage;
        }

        _logger.LogInformation(
            "Storage created. Id={StorageId}, Name={Name}, Type={StorageType}, Active={IsActive}",
            storage.Id,
            storage.Name,
            storage.StorageType,
            storage.IsActive);

        return Map(storage);
    }

    public async Task<IReadOnlyList<StorageDto>> GetStorageListAsync(
        CancellationToken cancellationToken = default)
    {
        var storages = await _storageRepository.GetAllAsync(cancellationToken);
        return storages
            .OrderByDescending(storage => storage.IsActive)
            .ThenBy(storage => storage.Name)
            .Select(Map)
            .ToList();
    }

    public async Task<StorageDto> UpdateStorageAsync(
        UpdateStorageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateName(request.Name);
        ValidatePhotoRoot(request.PhotoRoot);

        var storage = await _storageRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Storage '{request.Id}' was not found.");

        storage.Name = request.Name.Trim();
        storage.StorageType = request.StorageType;
        storage.PhotoRoot = NormalizePath(request.PhotoRoot);
        storage.UpdatedAt = DateTime.UtcNow;

        await _storageRepository.UpdateAsync(storage, cancellationToken);

        _logger.LogInformation("Storage updated. Id={StorageId}, Name={Name}", storage.Id, storage.Name);
        return Map(storage);
    }

    public async Task<StorageDto> UpdatePhotoRootAsync(
        Guid storageId,
        string photoRoot,
        CancellationToken cancellationToken = default)
    {
        ValidatePhotoRoot(photoRoot);
        var storage = await _storageRepository.GetByIdAsync(storageId, cancellationToken)
            ?? throw new InvalidOperationException($"Storage '{storageId}' was not found.");

        var beforePhotoRoot = storage.PhotoRoot;
        storage.PhotoRoot = NormalizePath(photoRoot);
        storage.UpdatedAt = DateTime.UtcNow;
        await _storageRepository.UpdateAsync(storage, cancellationToken);

        _logger.LogInformation(
            "Storage PhotoRoot updated. Id={StorageId}, BeforePhotoRoot={BeforePhotoRoot}, AfterPhotoRoot={AfterPhotoRoot}",
            storage.Id,
            beforePhotoRoot,
            storage.PhotoRoot);

        var refreshed = await _storageRepository.GetByIdAsync(storageId, cancellationToken) ?? storage;
        _logger.LogInformation(
            "Storage PhotoRoot verified in DB. Id={StorageId}, DbPhotoRoot={DbPhotoRoot}, IsActive={IsActive}",
            refreshed.Id,
            refreshed.PhotoRoot,
            refreshed.IsActive);

        if (!refreshed.IsActive)
        {
            _logger.LogInformation(
                "Storage PhotoRoot updated but inactive; activating. Id={StorageId}",
                storageId);
            return await SetActiveStorageAsync(storageId, cancellationToken);
        }

        return Map(refreshed);
    }

    public async Task<StorageDto> SetActiveStorageAsync(
        Guid storageId,
        CancellationToken cancellationToken = default)
    {
        var storages = await _storageRepository.GetAllAsync(cancellationToken);
        var target = storages.FirstOrDefault(storage => storage.Id == storageId)
            ?? throw new InvalidOperationException($"Storage '{storageId}' was not found.");

        foreach (var storage in storages)
        {
            var shouldBeActive = storage.Id == storageId;
            if (storage.IsActive == shouldBeActive)
            {
                continue;
            }

            storage.IsActive = shouldBeActive;
            storage.UpdatedAt = DateTime.UtcNow;
            await _storageRepository.UpdateAsync(storage, cancellationToken);
        }

        _logger.LogInformation("Active storage set. Id={StorageId}, Name={Name}", target.Id, target.Name);

        var refreshed = await _storageRepository.GetByIdAsync(storageId, cancellationToken) ?? target;
        return Map(refreshed);
    }

    public async Task<IReadOnlyList<StorageValidationIssue>> ValidateStoragesAsync(
        CancellationToken cancellationToken = default)
    {
        var storages = await _storageRepository.GetAllAsync(cancellationToken);
        return storages
            .Where(storage => !_fileAccessService.PhotoRootExists(storage.PhotoRoot))
            .Select(storage => new StorageValidationIssue
            {
                StorageId = storage.Id,
                StorageName = storage.Name,
                PhotoRoot = storage.PhotoRoot,
                IsActive = storage.IsActive
            })
            .ToList();
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Storage name is required.", nameof(name));
        }
    }

    private static void ValidatePhotoRoot(string photoRoot)
    {
        if (string.IsNullOrWhiteSpace(photoRoot))
        {
            throw new ArgumentException("Storage PhotoRoot is required.", nameof(photoRoot));
        }
    }

    private static string NormalizePath(string photoRoot)
    {
        return photoRoot.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static StorageDto Map(StorageEntity storage)
    {
        return new StorageDto
        {
            Id = storage.Id,
            Name = storage.Name,
            StorageType = storage.StorageType,
            PhotoRoot = storage.PhotoRoot,
            IsActive = storage.IsActive,
            CreatedAt = storage.CreatedAt,
            UpdatedAt = storage.UpdatedAt
        };
    }
}

public sealed class StorageValidationIssue
{
    public Guid StorageId { get; init; }

    public string StorageName { get; init; } = string.Empty;

    public string PhotoRoot { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}
