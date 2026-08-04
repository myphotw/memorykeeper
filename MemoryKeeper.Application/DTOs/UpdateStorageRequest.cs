using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application.DTOs;

public sealed class UpdateStorageRequest
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required StorageType StorageType { get; init; }

    public required string PhotoRoot { get; init; }
}
