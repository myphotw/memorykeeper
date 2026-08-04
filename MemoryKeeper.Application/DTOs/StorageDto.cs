using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application.DTOs;

public sealed class StorageDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public StorageType StorageType { get; init; }

    public string PhotoRoot { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }
}
