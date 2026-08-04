using System.Text.Json;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Time;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class TagService
{
    private const int MaxRecentTags = 10;

    private static readonly string[] DefaultColors =
    [
        "#E57373", "#F06292", "#BA68C8", "#9575CD",
        "#7986CB", "#64B5F6", "#4FC3F7", "#4DB6AC",
        "#81C784", "#AED581", "#FFD54F", "#FFB74D",
        "#A1887F", "#90A4AE"
    ];

    private readonly ITagRepository _tagRepository;
    private readonly IMediaTagRepository _mediaTagRepository;
    private readonly IMediaRepository _mediaRepository;
    private readonly IStorageRepository _storageRepository;
    private readonly ISettingRepository _settingRepository;
    private readonly IFileAccessService _fileAccessService;
    private readonly ILogger<TagService> _logger;

    public TagService(
        ITagRepository tagRepository,
        IMediaTagRepository mediaTagRepository,
        IMediaRepository mediaRepository,
        IStorageRepository storageRepository,
        ISettingRepository settingRepository,
        IFileAccessService fileAccessService,
        ILogger<TagService> logger)
    {
        _tagRepository = tagRepository;
        _mediaTagRepository = mediaTagRepository;
        _mediaRepository = mediaRepository;
        _storageRepository = storageRepository;
        _settingRepository = settingRepository;
        _fileAccessService = fileAccessService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TagDto>> GetTagListAsync(CancellationToken cancellationToken = default)
    {
        var tags = await _tagRepository.GetAllAsync(TagSource.User, cancellationToken);
        return tags
            .OrderByDescending(tag => tag.IsPinned)
            .ThenByDescending(tag => tag.UsageCount)
            .ThenBy(tag => tag.Name)
            .Select(tag => Map(tag))
            .ToList();
    }

    public async Task<IReadOnlyList<TagDto>> GetPopularTagsAsync(
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var tags = await _tagRepository.GetPopularAsync(take, TagSource.User, cancellationToken);
        return tags
            .OrderByDescending(tag => tag.IsPinned)
            .ThenByDescending(tag => tag.UsageCount)
            .ThenBy(tag => tag.Name)
            .Select(tag => Map(tag))
            .ToList();
    }

    public async Task<IReadOnlyList<TagDto>> SearchTagsAsync(
        string keyword,
        CancellationToken cancellationToken = default)
    {
        var tags = await _tagRepository.SearchAsync(keyword, TagSource.User, cancellationToken);
        return tags
            .OrderByDescending(tag => tag.IsPinned)
            .ThenByDescending(tag => tag.UsageCount)
            .ThenBy(tag => tag.Name)
            .Select(tag => Map(tag))
            .ToList();
    }

    public async Task<TagDto> CreateTagAsync(
        CreateTagRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = NormalizeName(request.Name);

        var existing = await _tagRepository.GetByNameAsync(name, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException($"Tag '{name}' already exists.");
        }

        var now = DateTime.UtcNow;
        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            Name = name,
            Color = string.IsNullOrWhiteSpace(request.Color) ? CreateRandomColor() : request.Color.Trim(),
            UsageCount = 0,
            Source = TagSource.User,
            IsPinned = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _tagRepository.AddAsync(tag, cancellationToken);
        _logger.LogInformation("Tag created. TagId={TagId}, Name={Name}", tag.Id, tag.Name);
        return Map(tag);
    }

    public async Task<TagDto> RenameTagAsync(
        RenameTagRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = NormalizeName(request.Name);

        var tag = await _tagRepository.GetByIdAsync(request.TagId, cancellationToken)
            ?? throw new InvalidOperationException($"Tag '{request.TagId}' was not found.");

        var duplicate = await _tagRepository.GetByNameAsync(name, cancellationToken);
        if (duplicate is not null && duplicate.Id != tag.Id)
        {
            throw new InvalidOperationException($"Tag '{name}' already exists.");
        }

        tag.Name = name;
        tag.UpdatedAt = DateTime.UtcNow;
        await _tagRepository.UpdateAsync(tag, cancellationToken);
        return Map(tag);
    }

    public async Task<TagDto> SetPinnedAsync(
        SetPinnedTagRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tag = await _tagRepository.GetByIdAsync(request.TagId, cancellationToken)
            ?? throw new InvalidOperationException($"Tag '{request.TagId}' was not found.");

        tag.IsPinned = request.IsPinned;
        tag.UpdatedAt = DateTime.UtcNow;
        await _tagRepository.UpdateAsync(tag, cancellationToken);
        return Map(tag);
    }

    public async Task DeleteTagAsync(Guid tagId, CancellationToken cancellationToken = default)
    {
        var tag = await _tagRepository.GetByIdAsync(tagId, cancellationToken)
            ?? throw new InvalidOperationException($"Tag '{tagId}' was not found.");

        await _mediaTagRepository.DeleteByTagIdAsync(tagId, cancellationToken);
        await _tagRepository.DeleteAsync(tag, cancellationToken);
        await RemoveFromRecentAsync(tagId, cancellationToken);
        _logger.LogInformation("Tag deleted. TagId={TagId}, Name={Name}", tag.Id, tag.Name);
    }

    public async Task AssignTagsAsync(
        AssignTagRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MediaIds.Count == 0)
        {
            throw new ArgumentException("At least one media id is required.", nameof(request));
        }

        var tagIds = new List<Guid>(request.TagIds ?? []);
        if (!string.IsNullOrWhiteSpace(request.NewTagName))
        {
            var created = await GetOrCreateTagAsync(
                request.NewTagName,
                request.NewTagColor,
                cancellationToken);
            tagIds.Add(created.Id);
        }

        if (tagIds.Count == 0)
        {
            throw new ArgumentException("At least one tag is required.", nameof(request));
        }

        var now = DateTime.UtcNow;
        var toAdd = new List<MediaTag>();
        var usageDelta = new Dictionary<Guid, int>();
        var touched = new List<Guid>();

        foreach (var mediaId in request.MediaIds.Distinct())
        {
            foreach (var tagId in tagIds.Distinct())
            {
                touched.Add(tagId);
                if (await _mediaTagRepository.ExistsAsync(mediaId, tagId, cancellationToken))
                {
                    continue;
                }

                toAdd.Add(new MediaTag
                {
                    Id = Guid.NewGuid(),
                    MediaId = mediaId,
                    TagId = tagId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                usageDelta[tagId] = usageDelta.GetValueOrDefault(tagId) + 1;
            }
        }

        if (toAdd.Count > 0)
        {
            await _mediaTagRepository.AddRangeAsync(toAdd, cancellationToken);
        }

        foreach (var (tagId, delta) in usageDelta)
        {
            await AdjustUsageCountAsync(tagId, delta, cancellationToken);
        }

        await TouchRecentAsync(touched.Distinct(), cancellationToken);
    }

    public async Task RemoveTagsAsync(
        RemoveTagRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MediaIds.Count == 0 || request.TagIds.Count == 0)
        {
            return;
        }

        var mediaIdSet = request.MediaIds.ToHashSet();
        var tagIdSet = request.TagIds.ToHashSet();
        var usageDelta = new Dictionary<Guid, int>();
        var toDelete = new List<MediaTag>();

        foreach (var mediaId in mediaIdSet)
        {
            var links = await _mediaTagRepository.GetByMediaIdAsync(mediaId, cancellationToken);
            foreach (var link in links.Where(item => tagIdSet.Contains(item.TagId)))
            {
                toDelete.Add(link);
                usageDelta[link.TagId] = usageDelta.GetValueOrDefault(link.TagId) - 1;
            }
        }

        if (toDelete.Count > 0)
        {
            await _mediaTagRepository.DeleteRangeAsync(toDelete, cancellationToken);
        }

        foreach (var (tagId, delta) in usageDelta)
        {
            await AdjustUsageCountAsync(tagId, delta, cancellationToken);
        }

        await TouchRecentAsync(tagIdSet, cancellationToken);
    }

    public async Task<IReadOnlyList<TagDto>> GetMediaTagsAsync(
        Guid mediaId,
        CancellationToken cancellationToken = default)
    {
        var links = await _mediaTagRepository.GetByMediaIdAsync(mediaId, cancellationToken);
        var result = new List<TagDto>();
        foreach (var link in links)
        {
            var tag = link.Tag ?? await _tagRepository.GetByIdAsync(link.TagId, cancellationToken);
            if (tag is null || tag.Source != TagSource.User)
            {
                continue;
            }

            result.Add(Map(tag, isAssigned: true));
        }

        return result
            .OrderByDescending(tag => tag.IsPinned)
            .ThenByDescending(tag => tag.UsageCount)
            .ThenBy(tag => tag.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<TagDto>> GetAssignableTagsForMediaAsync(
        IReadOnlyCollection<Guid> mediaIds,
        string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var state = await GetTagPickerStateAsync(mediaIds, keyword, forRemove: false, cancellationToken);
        return state.PinnedTags
            .Concat(state.RecentTags)
            .Concat(state.CommonTags)
            .Concat(state.CandidateTags)
            .GroupBy(tag => tag.Id)
            .Select(group => group.First())
            .ToList();
    }

    public async Task<TagPickerStateDto> GetTagPickerStateAsync(
        IReadOnlyCollection<Guid> mediaIds,
        string? keyword = null,
        bool forRemove = false,
        CancellationToken cancellationToken = default)
    {
        var assignedCounts = await GetAssignedCountsAsync(mediaIds, cancellationToken);
        var commonIds = assignedCounts
            .Where(pair => mediaIds.Count > 0 && pair.Value >= mediaIds.Count)
            .Select(pair => pair.Key)
            .ToHashSet();
        var anyAssignedIds = assignedCounts.Keys.ToHashSet();

        var allTags = await _tagRepository.GetAllAsync(TagSource.User, cancellationToken);
        var tagMap = allTags.ToDictionary(tag => tag.Id);

        bool Matches(Tag tag) =>
            string.IsNullOrWhiteSpace(keyword) ||
            tag.Name.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase);

        var pinned = allTags
            .Where(tag => tag.IsPinned)
            .Where(Matches)
            .Where(tag => !forRemove || anyAssignedIds.Contains(tag.Id))
            .OrderByDescending(tag => tag.UsageCount)
            .ThenBy(tag => tag.Name)
            .Select(tag => Map(tag, commonIds.Contains(tag.Id)))
            .ToList();
        var pinnedIds = pinned.Select(tag => tag.Id).ToHashSet();

        var recent = new List<TagDto>();
        foreach (var recentId in await LoadRecentIdsAsync(cancellationToken))
        {
            if (!tagMap.TryGetValue(recentId, out var tag) ||
                !Matches(tag) ||
                pinnedIds.Contains(tag.Id) ||
                (forRemove && !anyAssignedIds.Contains(tag.Id)))
            {
                continue;
            }

            recent.Add(Map(tag, commonIds.Contains(tag.Id)));
        }

        var recentIds = recent.Select(tag => tag.Id).ToHashSet();

        var common = commonIds
            .Select(id => tagMap.GetValueOrDefault(id))
            .Where(tag => tag is not null)
            .Cast<Tag>()
            .Where(Matches)
            .OrderByDescending(tag => tag.IsPinned)
            .ThenByDescending(tag => tag.UsageCount)
            .ThenBy(tag => tag.Name)
            .Select(tag => Map(tag, isAssigned: true))
            .ToList();

        IEnumerable<Tag> candidateSource;
        if (forRemove)
        {
            candidateSource = anyAssignedIds
                .Where(id => !commonIds.Contains(id))
                .Select(id => tagMap.GetValueOrDefault(id))
                .Where(tag => tag is not null)
                .Cast<Tag>()
                .Where(Matches);
        }
        else if (string.IsNullOrWhiteSpace(keyword))
        {
            candidateSource = allTags
                .OrderByDescending(tag => tag.UsageCount)
                .ThenBy(tag => tag.Name)
                .Take(50);
        }
        else
        {
            candidateSource = allTags.Where(Matches);
        }

        var excludedFromCandidates = pinnedIds
            .Concat(recentIds)
            .Concat(common.Select(tag => tag.Id))
            .ToHashSet();

        var candidates = candidateSource
            .Where(tag => !excludedFromCandidates.Contains(tag.Id))
            .OrderByDescending(tag => tag.UsageCount)
            .ThenBy(tag => tag.Name)
            .Select(tag => Map(tag, commonIds.Contains(tag.Id)))
            .ToList();

        return new TagPickerStateDto
        {
            PinnedTags = pinned,
            RecentTags = recent,
            CommonTags = common,
            CandidateTags = candidates
        };
    }

    public async Task<IReadOnlyList<GalleryMediaDto>> SearchByTagAsync(
        IReadOnlyCollection<Guid> tagIds,
        int? year = null,
        Guid? placeId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid>? mediaIds = null;
        if (tagIds.Count > 0)
        {
            mediaIds = await _mediaTagRepository.GetMediaIdsWithAllTagsAsync(tagIds, cancellationToken);
            if (mediaIds.Count == 0)
            {
                return [];
            }
        }

        var mediaItems = await _mediaRepository.SearchAsync(year, placeId, placeIds: null, cancellationToken);
        if (mediaIds is not null)
        {
            var idSet = mediaIds.ToHashSet();
            mediaItems = mediaItems.Where(media => idSet.Contains(media.Id)).ToList();
        }

        var storages = (await _storageRepository.GetAllAsync(cancellationToken))
            .ToDictionary(storage => storage.Id);

        return mediaItems
            .Where(media => media.MediaType == MediaType.Photo)
            .Where(media => storages.ContainsKey(media.StorageId))
            .Select(media => new GalleryMediaDto
            {
                Id = media.Id,
                FileName = media.FileName,
                AbsoluteLibraryPath = _fileAccessService.ResolveAbsolutePath(
                    storages[media.StorageId].PhotoRoot,
                    media.RelativePath),
                CapturedAt = DateTimeHelper.ToUtcOffset(media.CapturedAt),
                PlaceId = media.PlaceId,
                MediaType = media.MediaType,
                IsFavorite = media.IsFavorite
            })
            .ToList();
    }

    private async Task<Dictionary<Guid, int>> GetAssignedCountsAsync(
        IReadOnlyCollection<Guid> mediaIds,
        CancellationToken cancellationToken)
    {
        if (mediaIds.Count == 0)
        {
            return [];
        }

        var links = await _mediaTagRepository.GetByMediaIdsAsync(mediaIds, cancellationToken);
        return links
            .GroupBy(link => link.TagId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.MediaId).Distinct().Count());
    }

    private async Task<TagDto> GetOrCreateTagAsync(
        string name,
        string? color,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeName(name);
        var existing = await _tagRepository.GetByNameAsync(normalized, cancellationToken);
        if (existing is not null)
        {
            return Map(existing);
        }

        return await CreateTagAsync(new CreateTagRequest
        {
            Name = normalized,
            Color = color
        }, cancellationToken);
    }

    private async Task AdjustUsageCountAsync(Guid tagId, int delta, CancellationToken cancellationToken)
    {
        if (delta == 0)
        {
            return;
        }

        var tag = await _tagRepository.GetByIdAsync(tagId, cancellationToken);
        if (tag is null)
        {
            return;
        }

        tag.UsageCount = Math.Max(0, tag.UsageCount + delta);
        tag.UpdatedAt = DateTime.UtcNow;
        await _tagRepository.UpdateAsync(tag, cancellationToken);
    }

    private async Task TouchRecentAsync(
        IEnumerable<Guid> tagIds,
        CancellationToken cancellationToken)
    {
        var recent = await LoadRecentIdsAsync(cancellationToken);
        foreach (var tagId in tagIds.Distinct())
        {
            recent.Remove(tagId);
            recent.Insert(0, tagId);
        }

        if (recent.Count > MaxRecentTags)
        {
            recent = recent.Take(MaxRecentTags).ToList();
        }

        await SaveRecentIdsAsync(recent, cancellationToken);
    }

    private async Task RemoveFromRecentAsync(Guid tagId, CancellationToken cancellationToken)
    {
        var recent = await LoadRecentIdsAsync(cancellationToken);
        if (!recent.Remove(tagId))
        {
            return;
        }

        await SaveRecentIdsAsync(recent, cancellationToken);
    }

    private async Task<List<Guid>> LoadRecentIdsAsync(CancellationToken cancellationToken)
    {
        var setting = await _settingRepository.GetByKeyAsync(SettingKeys.RecentTagIds, cancellationToken);
        if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
        {
            return [];
        }

        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(setting.Value) ?? [];
            return values
                .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Take(MaxRecentTags)
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse recent tag setting.");
            return [];
        }
    }

    private async Task SaveRecentIdsAsync(IReadOnlyList<Guid> tagIds, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(tagIds.Select(id => id.ToString("D")).ToList());
        var existing = await _settingRepository.GetByKeyAsync(SettingKeys.RecentTagIds, cancellationToken);
        var now = DateTime.UtcNow;

        if (existing is null)
        {
            await _settingRepository.AddAsync(new Setting
            {
                Id = Guid.NewGuid(),
                Key = SettingKeys.RecentTagIds,
                Value = payload,
                CreatedAt = now,
                UpdatedAt = now
            }, cancellationToken);
            return;
        }

        existing.Value = payload;
        existing.UpdatedAt = now;
        await _settingRepository.UpdateAsync(existing, cancellationToken);
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tag name is required.", nameof(name));
        }

        return name.Trim();
    }

    private static string CreateRandomColor()
    {
        return DefaultColors[Random.Shared.Next(DefaultColors.Length)];
    }

    private static TagDto Map(Tag tag, bool isAssigned = false)
    {
        return new TagDto
        {
            Id = tag.Id,
            Name = tag.Name,
            Color = tag.Color,
            UsageCount = tag.UsageCount,
            Source = tag.Source,
            IsPinned = tag.IsPinned,
            IsAssigned = isAssigned
        };
    }
}
