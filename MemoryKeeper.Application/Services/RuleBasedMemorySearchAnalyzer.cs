using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Rule-based natural language → structured memory search filters.
/// Future: replace via DI with AiMemorySearchAnalyzer without changing MemorySearchService.
/// </summary>
public sealed class RuleBasedMemorySearchAnalyzer : IMemorySearchAnalyzer
{
    private static readonly HashSet<string> FavoriteTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "즐겨찾기",
        "favorite",
        "favorites",
        "★",
        "⭐"
    };

    private readonly IPlaceRepository _placeRepository;
    private readonly ITagRepository _tagRepository;
    private readonly ILogger<RuleBasedMemorySearchAnalyzer> _logger;

    public RuleBasedMemorySearchAnalyzer(
        IPlaceRepository placeRepository,
        ITagRepository tagRepository,
        ILogger<RuleBasedMemorySearchAnalyzer> logger)
    {
        _placeRepository = placeRepository;
        _tagRepository = tagRepository;
        _logger = logger;
    }

    public async Task<MemorySearchAnalysis> AnalyzeAsync(
        string searchText,
        CancellationToken cancellationToken = default)
    {
        var tokens = Tokenize(searchText);
        if (tokens.Count == 0)
        {
            return new MemorySearchAnalysis();
        }

        var remaining = new List<string>(tokens);
        int? year = null;
        var favoriteOnly = false;
        Guid? placeId = null;
        string? placeLabel = null;
        var tagIds = new List<Guid>();
        var tagLabels = new List<string>();
        var chips = new List<MemorySearchChipDto>();

        // 1. Year
        for (var i = remaining.Count - 1; i >= 0; i--)
        {
            if (!TryParseYear(remaining[i], out var parsedYear, out var yearLabel))
            {
                continue;
            }

            year = parsedYear;
            chips.Add(new MemorySearchChipDto
            {
                Label = yearLabel,
                Kind = MemorySearchChipKind.Year
            });
            remaining.RemoveAt(i);
            break;
        }

        // 2. Favorite
        for (var i = remaining.Count - 1; i >= 0; i--)
        {
            if (!FavoriteTokens.Contains(remaining[i]))
            {
                continue;
            }

            favoriteOnly = true;
            chips.Add(new MemorySearchChipDto
            {
                Label = "즐겨찾기",
                Kind = MemorySearchChipKind.Favorite
            });
            remaining.RemoveAt(i);
        }

        // 3. Place (first matching token wins)
        for (var i = 0; i < remaining.Count; i++)
        {
            var token = remaining[i];
            var place = await FindBestPlaceAsync(token, cancellationToken);
            if (place is null)
            {
                continue;
            }

            placeId = place.Id;
            placeLabel = place.DisplayName;
            chips.Add(new MemorySearchChipDto
            {
                Label = place.DisplayName,
                Kind = MemorySearchChipKind.Place
            });
            remaining.RemoveAt(i);
            break;
        }

        // 4. Tag (each remaining token may become a tag)
        var stillIgnored = new List<string>();
        foreach (var token in remaining)
        {
            var tag = await FindBestTagAsync(token, cancellationToken);
            if (tag is null)
            {
                stillIgnored.Add(token);
                continue;
            }

            if (tagIds.Contains(tag.Id))
            {
                continue;
            }

            tagIds.Add(tag.Id);
            tagLabels.Add(tag.Name);
            chips.Add(new MemorySearchChipDto
            {
                Label = tag.Name,
                Kind = MemorySearchChipKind.Tag
            });
        }

        if (stillIgnored.Count > 0)
        {
            _logger.LogInformation(
                "Memory search ignored unmatched tokens. Tokens={Tokens}",
                string.Join(", ", stillIgnored));
        }

        // Display order: Place → Tag → Year → Favorite
        chips = chips
            .OrderBy(chip => chip.Kind switch
            {
                MemorySearchChipKind.Place => 0,
                MemorySearchChipKind.Tag => 1,
                MemorySearchChipKind.Year => 2,
                MemorySearchChipKind.Favorite => 3,
                _ => 9
            })
            .ThenBy(chip => chip.Label)
            .ToList();

        return new MemorySearchAnalysis
        {
            Request = new MemorySearchRequest
            {
                SearchText = searchText.Trim(),
                Year = year,
                PlaceId = placeId,
                TagIds = tagIds.Count > 0 ? tagIds : null,
                FavoriteOnly = favoriteOnly
            },
            Chips = chips
        };
    }

    private async Task<Place?> FindBestPlaceAsync(string token, CancellationToken cancellationToken)
    {
        var matches = await _placeRepository.SearchAsync(token, cancellationToken);
        return PickBestPlace(matches, token);
    }

    private async Task<Tag?> FindBestTagAsync(string token, CancellationToken cancellationToken)
    {
        var matches = await _tagRepository.SearchAsync(token, TagSource.User, cancellationToken);
        return PickBestTag(matches, token);
    }

    internal static Place? PickBestPlace(IReadOnlyList<Place> matches, string token)
    {
        if (matches.Count == 0)
        {
            return null;
        }

        return matches
            .OrderBy(place => ScoreNameMatch(place.DisplayName, token))
            .ThenBy(place => place.DisplayName.Length)
            .ThenBy(place => place.DisplayName)
            .First();
    }

    internal static Tag? PickBestTag(IReadOnlyList<Tag> matches, string token)
    {
        if (matches.Count == 0)
        {
            return null;
        }

        return matches
            .OrderBy(tag => ScoreNameMatch(tag.Name, token))
            .ThenByDescending(tag => tag.UsageCount)
            .ThenBy(tag => tag.Name)
            .First();
    }

    /// <summary>
    /// Lower score is better: 0 exact, 1 starts-with, 2 contains.
    /// </summary>
    private static int ScoreNameMatch(string name, string token)
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

    public static bool TryParseYear(string token, out int year, out string label)
    {
        year = 0;
        label = token;
        var current = DateTime.Now.Year;

        if (string.Equals(token, "올해", StringComparison.OrdinalIgnoreCase))
        {
            year = current;
            label = current.ToString();
            return true;
        }

        if (string.Equals(token, "작년", StringComparison.OrdinalIgnoreCase))
        {
            year = current - 1;
            label = year.ToString();
            return true;
        }

        if (string.Equals(token, "재작년", StringComparison.OrdinalIgnoreCase))
        {
            year = current - 2;
            label = year.ToString();
            return true;
        }

        if (int.TryParse(token, out var parsed) && parsed is >= 1900 and <= 2100)
        {
            year = parsed;
            label = parsed.ToString();
            return true;
        }

        return false;
    }

    private static List<string> Tokenize(string searchText)
    {
        return searchText
            .Split([' ', '\t', '\r', '\n', ',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToList();
    }
}
