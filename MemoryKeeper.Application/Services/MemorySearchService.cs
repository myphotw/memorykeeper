using System.Text.Json;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class MemorySearchService
{
    private const int MaxRecentQueries = 10;
    private const int MaxSuggestions = 8;

    private readonly IMediaRepository _mediaRepository;
    private readonly IPlaceRepository _placeRepository;
    private readonly IMediaTagRepository _mediaTagRepository;
    private readonly ITagRepository _tagRepository;
    private readonly ISettingRepository _settingRepository;
    private readonly IMemorySearchAnalyzer _analyzer;
    private readonly VisitRecordService _visitRecordService;
    private readonly ILogger<MemorySearchService> _logger;

    public MemorySearchService(
        IMediaRepository mediaRepository,
        IPlaceRepository placeRepository,
        IMediaTagRepository mediaTagRepository,
        ITagRepository tagRepository,
        ISettingRepository settingRepository,
        IMemorySearchAnalyzer analyzer,
        VisitRecordService visitRecordService,
        ILogger<MemorySearchService> logger)
    {
        _mediaRepository = mediaRepository;
        _placeRepository = placeRepository;
        _mediaTagRepository = mediaTagRepository;
        _tagRepository = tagRepository;
        _settingRepository = settingRepository;
        _analyzer = analyzer;
        _visitRecordService = visitRecordService;
        _logger = logger;
    }

    public async Task<MemorySearchQueryResult> SearchAsync(
        MemorySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (resolved, chips) = await ResolveRequestAsync(request, cancellationToken);
        var items = await ExecuteSearchAsync(resolved, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            await SaveRecentQueryAsync(request.SearchText.Trim(), cancellationToken);
        }

        _logger.LogInformation(
            "Memory search completed. SearchText={SearchText}, Year={Year}, PlaceId={PlaceId}, TagCount={TagCount}, FavoriteOnly={FavoriteOnly}, ResultCount={ResultCount}",
            resolved.SearchText,
            resolved.Year,
            resolved.PlaceId,
            resolved.TagIds?.Count ?? 0,
            resolved.FavoriteOnly,
            items.Count);

        return new MemorySearchQueryResult
        {
            Items = items,
            Chips = chips,
            ResolvedRequest = resolved
        };
    }

    public async Task<IReadOnlyList<MemorySearchSuggestionDto>> SuggestAsync(
        string partialText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(partialText))
        {
            return [];
        }

        var token = partialText
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault()
            ?? partialText.Trim();

        if (string.IsNullOrWhiteSpace(token))
        {
            return [];
        }

        var places = await _placeRepository.SearchAsync(token, cancellationToken);
        var tags = await _tagRepository.SearchAsync(token, Domain.Enums.TagSource.User, cancellationToken);

        var suggestions = new List<MemorySearchSuggestionDto>();

        foreach (var place in places
                     .OrderBy(item => GetSuggestionRank(item.DisplayName, token))
                     .ThenBy(item => item.DisplayName.Length)
                     .ThenBy(item => item.DisplayName)
                     .Take(MaxSuggestions / 2))
        {
            suggestions.Add(new MemorySearchSuggestionDto
            {
                Text = place.DisplayName,
                Kind = MemorySearchSuggestionKind.Place
            });
        }

        foreach (var tag in tags
                     .OrderBy(item => GetSuggestionRank(item.Name, token))
                     .ThenByDescending(item => item.UsageCount)
                     .ThenBy(item => item.Name)
                     .Take(MaxSuggestions))
        {
            if (suggestions.Count >= MaxSuggestions)
            {
                break;
            }

            suggestions.Add(new MemorySearchSuggestionDto
            {
                Text = tag.Name,
                Kind = MemorySearchSuggestionKind.Tag
            });
        }

        return suggestions
            .GroupBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(MaxSuggestions)
            .ToList();
    }

    private static int GetSuggestionRank(string name, string token)
    {
        if (string.Equals(name, token, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    public async Task<IReadOnlyList<string>> GetRecentQueriesAsync(
        CancellationToken cancellationToken = default)
    {
        return await LoadRecentQueriesAsync(cancellationToken);
    }

    public Task ClearRecentQueriesAsync(CancellationToken cancellationToken = default)
    {
        return SaveRecentQueriesAsync([], cancellationToken);
    }

    private async Task<(MemorySearchRequest Resolved, IReadOnlyList<MemorySearchChipDto> Chips)> ResolveRequestAsync(
        MemorySearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            var analysis = await _analyzer.AnalyzeAsync(request.SearchText, cancellationToken);
            var analyzed = analysis.Request;

            // Explicit structured filters override analyzer when provided.
            var resolved = new MemorySearchRequest
            {
                SearchText = request.SearchText.Trim(),
                Year = request.Year ?? analyzed.Year,
                PlaceId = request.PlaceId ?? analyzed.PlaceId,
                Keyword = request.Keyword ?? analyzed.Keyword,
                TagIds = request.TagIds ?? analyzed.TagIds,
                FavoriteOnly = request.FavoriteOnly || analyzed.FavoriteOnly
            };

            var chips = analysis.Chips.Count > 0
                ? analysis.Chips
                : await BuildChipsAsync(resolved, cancellationToken);

            return (resolved, chips);
        }

        var structured = new MemorySearchRequest
        {
            Year = request.Year,
            PlaceId = request.PlaceId,
            Keyword = request.Keyword,
            TagIds = request.TagIds,
            FavoriteOnly = request.FavoriteOnly
        };

        return (structured, await BuildChipsAsync(structured, cancellationToken));
    }

    private async Task<IReadOnlyList<MemorySearchResult>> ExecuteSearchAsync(
        MemorySearchRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid>? keywordPlaceIds = null;
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var matchedPlaces = await _placeRepository.SearchAsync(request.Keyword.Trim(), cancellationToken);
            keywordPlaceIds = matchedPlaces.Select(place => place.Id).ToList();
            if (keywordPlaceIds.Count == 0)
            {
                _logger.LogInformation("Memory search found no places for keyword. Keyword={Keyword}", request.Keyword);
                return [];
            }
        }

        var rows = await _mediaRepository.SearchAsync(
            request.Year,
            request.PlaceId,
            keywordPlaceIds,
            cancellationToken);

        if (request.FavoriteOnly)
        {
            rows = rows.Where(media => media.IsFavorite).ToList();
        }

        if (request.TagIds is { Count: > 0 })
        {
            var mediaIdsWithTags = await _mediaTagRepository.GetMediaIdsWithAllTagsAsync(
                request.TagIds,
                cancellationToken);
            var idSet = mediaIdsWithTags.ToHashSet();
            rows = rows.Where(media => idSet.Contains(media.Id)).ToList();
        }

        if (rows.Count == 0)
        {
            return [];
        }

        var placeIds = rows
            .Where(media => media.PlaceId.HasValue)
            .Select(media => media.PlaceId!.Value)
            .Distinct()
            .ToList();

        var places = await _placeRepository.GetByIdsAsync(placeIds, cancellationToken);
        var placeMap = places.ToDictionary(place => place.Id);

        return rows
            .Where(media => media.PlaceId.HasValue && placeMap.ContainsKey(media.PlaceId.Value))
            .GroupBy(media => media.PlaceId!.Value)
            .Select(group =>
            {
                var place = placeMap[group.Key];
                var visitDates = group
                    .Select(media => _visitRecordService.ResolveVisitDate(media.CapturedAt, media.ImportedAt))
                    .ToList();

                var ordered = group
                    .OrderByDescending(media => media.IsFavorite)
                    .ThenByDescending(media => media.CapturedAt)
                    .ThenByDescending(media => media.ImportedAt)
                    .ToList();

                return new MemorySearchResult
                {
                    PlaceId = place.Id,
                    PlaceName = place.DisplayName,
                    Country = place.Country,
                    City = place.City,
                    PhotoCount = group.Count(),
                    VisitRecordCount = _visitRecordService.CalculateVisitRecordCount(visitDates),
                    FavoriteCount = group.Count(media => media.IsFavorite),
                    RepresentativeMediaId = ordered.FirstOrDefault()?.Id,
                    FirstCapturedDate = visitDates.Min(),
                    LastCapturedDate = visitDates.Max()
                };
            })
            .OrderByDescending(result => result.LastCapturedDate)
            .ThenBy(result => result.PlaceName)
            .ToList();
    }

    private async Task<IReadOnlyList<MemorySearchChipDto>> BuildChipsAsync(
        MemorySearchRequest request,
        CancellationToken cancellationToken)
    {
        var chips = new List<MemorySearchChipDto>();

        if (request.Year is int year)
        {
            chips.Add(new MemorySearchChipDto
            {
                Label = year.ToString(),
                Kind = MemorySearchChipKind.Year
            });
        }

        if (request.FavoriteOnly)
        {
            chips.Add(new MemorySearchChipDto
            {
                Label = "즐겨찾기",
                Kind = MemorySearchChipKind.Favorite
            });
        }

        if (request.PlaceId is Guid placeId)
        {
            var place = await _placeRepository.GetByIdAsync(placeId, cancellationToken);
            if (place is not null)
            {
                chips.Add(new MemorySearchChipDto
                {
                    Label = place.DisplayName,
                    Kind = MemorySearchChipKind.Place
                });
            }
        }

        if (request.TagIds is { Count: > 0 })
        {
            foreach (var tagId in request.TagIds)
            {
                var tag = await _tagRepository.GetByIdAsync(tagId, cancellationToken);
                if (tag is null)
                {
                    continue;
                }

                chips.Add(new MemorySearchChipDto
                {
                    Label = tag.Name,
                    Kind = MemorySearchChipKind.Tag
                });
            }
        }

        return chips;
    }

    private async Task SaveRecentQueryAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var recent = await LoadRecentQueriesAsync(cancellationToken);
        recent.RemoveAll(item => string.Equals(item, query, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, query);
        if (recent.Count > MaxRecentQueries)
        {
            recent = recent.Take(MaxRecentQueries).ToList();
        }

        await SaveRecentQueriesAsync(recent, cancellationToken);
    }

    private async Task<List<string>> LoadRecentQueriesAsync(CancellationToken cancellationToken)
    {
        var setting = await _settingRepository.GetByKeyAsync(SettingKeys.RecentSearchQueries, cancellationToken);
        if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(setting.Value)
                       ?.Where(item => !string.IsNullOrWhiteSpace(item))
                       .Select(item => item.Trim())
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .Take(MaxRecentQueries)
                       .ToList()
                   ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse recent search queries.");
            return [];
        }
    }

    private async Task SaveRecentQueriesAsync(IReadOnlyList<string> queries, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(queries.ToList());
        var existing = await _settingRepository.GetByKeyAsync(SettingKeys.RecentSearchQueries, cancellationToken);
        var now = DateTime.UtcNow;

        if (existing is null)
        {
            await _settingRepository.AddAsync(new Setting
            {
                Id = Guid.NewGuid(),
                Key = SettingKeys.RecentSearchQueries,
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
}
