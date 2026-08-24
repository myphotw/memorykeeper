using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application.DTOs;

public sealed class TagDto
{
    public Guid Id { get; init; }

    /// <summary>Canonical tc-backend tag id. Null only for legacy/local or Vision-only tags.</summary>
    public int? BackendId { get; init; }

    /// <summary>Opaque Backend catalog identity used only for mutations and selection.</summary>
    public string? Identity { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Color { get; init; } = string.Empty;

    public int UsageCount { get; init; }

    public TagSource Source { get; init; }

    public bool IsPinned { get; init; }

    public bool IsAssigned { get; init; }

    public int Revision { get; init; }

    public bool CanRemove { get; init; } = true;
}

public sealed class CreateTagRequest
{
    public required string Name { get; init; }

    public string? Color { get; init; }
}

public sealed class RenameTagRequest
{
    public required Guid TagId { get; init; }

    public required string Name { get; init; }
}

public sealed class SetPinnedTagRequest
{
    public required Guid TagId { get; init; }

    public required bool IsPinned { get; init; }
}

public sealed class AssignTagRequest
{
    public required IReadOnlyList<Guid> MediaIds { get; init; }

    public IReadOnlyList<Guid>? TagIds { get; init; }

    public string? NewTagName { get; init; }

    public string? NewTagColor { get; init; }
}

public sealed class RemoveTagRequest
{
    public required IReadOnlyList<Guid> MediaIds { get; init; }

    public required IReadOnlyList<Guid> TagIds { get; init; }
}

public sealed class TagPickerStateDto
{
    public IReadOnlyList<TagDto> PinnedTags { get; init; } = [];

    public IReadOnlyList<TagDto> RecentTags { get; init; } = [];

    public IReadOnlyList<TagDto> CommonTags { get; init; } = [];

    public IReadOnlyList<TagDto> CandidateTags { get; init; } = [];
}
