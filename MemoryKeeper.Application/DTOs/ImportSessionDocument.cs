namespace MemoryKeeper.Application.DTOs;

/// <summary>Persisted open upload jobs for app restart resume (JSON, not SQLite).</summary>
public sealed class ImportSessionDocument
{
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ImportSessionJobDto> Jobs { get; set; } = [];
}

public sealed class ImportSessionJobDto
{
    public string JobId { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string LocalFilePath { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset? UploadedAt { get; set; }

    public string? ContentHash { get; set; }
}
