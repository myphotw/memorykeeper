using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application.DTOs;

public sealed class CreateStorageRequest
{
    public required string Name { get; init; }

    public required StorageType StorageType { get; init; }

    public required string PhotoRoot { get; init; }

    public bool SetAsActive { get; init; } = true;
}
