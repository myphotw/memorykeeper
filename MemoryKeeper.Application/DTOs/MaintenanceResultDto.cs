namespace MemoryKeeper.Application.DTOs;

public sealed class MaintenanceResultDto
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? OutputPath { get; init; }
}
