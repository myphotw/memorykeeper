using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class HomeDashboardService
{
    private const int LookbackYears = 10;
    private const int TodayPhotoTake = 5;
    private const int RecentVisitTake = 5;
    private const int FavoriteTake = 12;
    private const int RecentImportTake = 12;
    private const int RecentQueryTake = 5;
    private const int TopTagTake = 2;

    private readonly IDashboardRepository _dashboardRepository;
    private readonly IMediaRepository _mediaRepository;
    private readonly IMediaTagRepository _mediaTagRepository;
    private readonly ITagRepository _tagRepository;
    private readonly IStorageRepository _storageRepository;
    private readonly IFileAccessService _fileAccessService;
    private readonly MemorySearchService _memorySearchService;
    private readonly VisitRecordService _visitRecordService;
    private readonly ILogger<HomeDashboardService> _logger;

    public HomeDashboardService(
        IDashboardRepository dashboardRepository,
        IMediaRepository mediaRepository,
        IMediaTagRepository mediaTagRepository,
        ITagRepository tagRepository,
        IStorageRepository storageRepository,
        IFileAccessService fileAccessService,
        MemorySearchService memorySearchService,
        VisitRecordService visitRecordService,
        ILogger<HomeDashboardService> logger)
    {
        _dashboardRepository = dashboardRepository;
        _mediaRepository = mediaRepository;
        _mediaTagRepository = mediaTagRepository;
        _tagRepository = tagRepository;
        _storageRepository = storageRepository;
        _fileAccessService = fileAccessService;
        _memorySearchService = memorySearchService;
        _visitRecordService = visitRecordService;
        _logger = logger;
    }

    public async Task<HomeDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.Now;

        var onThisDayTask = _dashboardRepository.GetOnThisDayPhotosAsync(
            today.Month, today.Day, LookbackYears, cancellationToken);
        var favoritesTask = _dashboardRepository.GetFavoritePhotosAsync(FavoriteTake, cancellationToken);
        var importsTask = _dashboardRepository.GetRecentImportsAsync(RecentImportTake, cancellationToken);
        var statsTask = _dashboardRepository.GetStatisticsAsync(cancellationToken);
        var pendingTask = _dashboardRepository.GetPendingBreakdownAsync(cancellationToken);
        var recentSearchTask = _memorySearchService.SearchAsync(new MemorySearchRequest(), cancellationToken);
        var recentQueriesTask = _memorySearchService.GetRecentQueriesAsync(cancellationToken);
        var storagesTask = _storageRepository.GetAllAsync(cancellationToken);

        await Task.WhenAll(
            onThisDayTask,
            favoritesTask,
            importsTask,
            statsTask,
            pendingTask,
            recentSearchTask,
            recentQueriesTask,
            storagesTask);

        var storages = (await storagesTask).ToDictionary(storage => storage.Id);
        var onThisDay = await onThisDayTask;
        var favorites = await favoritesTask;
        var imports = await importsTask;
        var recentSearch = await recentSearchTask;

        var mediaIdsForTags = onThisDay.Select(media => media.Id)
            .Concat(favorites.Select(media => media.Id))
            .Concat(imports.Select(media => media.Id))
            .Concat(recentSearch.Items
                .Take(RecentVisitTake)
                .Where(item => item.RepresentativeMediaId.HasValue)
                .Select(item => item.RepresentativeMediaId!.Value))
            .Distinct()
            .ToList();

        var tagLookup = await BuildTagLookupAsync(mediaIdsForTags, cancellationToken);
        var recentVisits = await BuildRecentVisitsAsync(recentSearch, storages, tagLookup, cancellationToken);

        return new HomeDashboardDto
        {
            HeroMemories = BuildHeroMemories(
                onThisDay,
                recentVisits,
                favorites,
                recentSearch,
                storages,
                tagLookup),
            TodayMemories = BuildTodayMemories(onThisDay, storages, tagLookup),
            RecentVisits = recentVisits,
            Favorites = MapPhotos(favorites, storages),
            RecentImports = MapPhotos(imports, storages),
            PendingSummary = await MapPendingAsync(await pendingTask, storages, cancellationToken),
            RecentQueries = (await recentQueriesTask).Take(RecentQueryTake).ToList(),
            Statistics = MapStats(await statsTask)
        };
    }

    private IReadOnlyList<HeroMemoryDto> BuildHeroMemories(
        IReadOnlyList<Media> onThisDay,
        IReadOnlyList<RecentVisitDto> recentVisits,
        IReadOnlyList<Media> favorites,
        MemorySearchQueryResult recentSearch,
        IReadOnlyDictionary<Guid, Storage> storages,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagLookup)
    {
        // 추천 추억만 Hero에 쓴다. "최근 여행 나열"이 아니라 다시 보고 싶은 기억을 고른다.

        // 1. 오늘의 추억 — 가장 강한 추천
        var todayHero = TryBuildOnThisDayHero(onThisDay, storages, tagLookup);
        if (todayHero is not null)
        {
            return [todayHero];
        }

        // 2. 마음에 담아 둔 사진
        var favoriteHero = TryBuildFavoriteHero(favorites, storages, tagLookup);
        if (favoriteHero is not null)
        {
            return [favoriteHero];
        }

        // 3. 자주 찾은 곳 — 다시 만나고 싶은 장소
        var mostVisitedPlaceId = recentSearch.Items
            .OrderByDescending(item => item.VisitRecordCount)
            .ThenByDescending(item => item.LastCapturedDate)
            .Select(item => item.PlaceId)
            .FirstOrDefault();
        var mostVisited = recentVisits.FirstOrDefault(item =>
            item.PlaceId == mostVisitedPlaceId
            && !string.IsNullOrWhiteSpace(item.AbsoluteLibraryPath))
            ?? recentVisits
                .Where(item => !string.IsNullOrWhiteSpace(item.AbsoluteLibraryPath))
                .OrderByDescending(item => item.VisitRecordCount)
                .FirstOrDefault();
        if (mostVisited is not null)
        {
            return [FromRecentVisit(
                mostVisited,
                kindLabel: "추천 추억",
                description: string.IsNullOrWhiteSpace(mostVisited.PlaceName)
                    ? "자주 찾던 곳을 다시 만나 보세요."
                    : $"{mostVisited.PlaceName}에서의 날을 다시 만나 보세요.")];
        }

        // 4. 최근 방문 중 하나를 부드럽게 추천 (목록이 아니라 회상 제안)
        var softRecommend = recentVisits.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.AbsoluteLibraryPath));
        if (softRecommend is not null)
        {
            return [FromRecentVisit(
                softRecommend,
                kindLabel: "추천 추억",
                description: string.IsNullOrWhiteSpace(softRecommend.PlaceName)
                    ? "다시 보고 싶은 여행을 골라 두었어요."
                    : $"{softRecommend.PlaceName}, 다시 보고 싶지 않나요?")];
        }

        return [];
    }

    private HeroMemoryDto? TryBuildOnThisDayHero(
        IReadOnlyList<Media> onThisDay,
        IReadOnlyDictionary<Guid, Storage> storages,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagLookup)
    {
        var currentYear = DateTime.Now.Year;
        return onThisDay
            .Where(media => media.PlaceId is not null && media.CapturedAt is not null)
            .GroupBy(media => new { PlaceId = media.PlaceId!.Value, Year = media.CapturedAt!.Value.Year })
            .Select(group =>
            {
                var ordered = group
                    .OrderByDescending(media => media.IsFavorite)
                    .ThenByDescending(media => media.CapturedAt)
                    .ToList();
                var rep = ordered[0];
                var path = ResolvePath(rep, storages);
                if (string.IsNullOrWhiteSpace(path))
                {
                    return null;
                }

                var yearsAgo = Math.Max(1, currentYear - group.Key.Year);
                var placeName = rep.Place?.DisplayName ?? string.Empty;
                return new HeroMemoryDto
                {
                    PlaceId = group.Key.PlaceId,
                    PlaceName = placeName,
                    Year = group.Key.Year,
                    YearsAgo = yearsAgo,
                    PhotoCount = ordered.Count,
                    VisitRecordCount = _visitRecordService.CalculateVisitRecordCount(
                        ordered.Select(media => (media.CapturedAt, media.ImportedAt))),
                    RepresentativeMediaId = rep.Id,
                    AbsoluteLibraryPath = path,
                    TopTags = (tagLookup.GetValueOrDefault(rep.Id) ?? []).Take(TopTagTake).ToList(),
                    KindLabel = "추천 추억",
                    DateText = $"{yearsAgo}년 전 오늘",
                    Description = string.IsNullOrWhiteSpace(placeName)
                        ? "그날의 사진을 다시 만나 보세요."
                        : $"{placeName}에서의 하루를 다시 만나 보세요."
                };
            })
            .Where(item => item is not null)
            .Cast<HeroMemoryDto>()
            .OrderByDescending(item => item.PhotoCount)
            .ThenByDescending(item => item.YearsAgo)
            .FirstOrDefault();
    }

    private static HeroMemoryDto FromRecentVisit(
        RecentVisitDto visit,
        string kindLabel,
        string description)
    {
        var dateText = visit.LastVisitDate?.ToLocalTime().ToString("yyyy.MM.dd") ?? string.Empty;
        return new HeroMemoryDto
        {
            PlaceId = visit.PlaceId,
            PlaceName = visit.PlaceName,
            Year = visit.LastVisitDate?.ToLocalTime().Year ?? 0,
            YearsAgo = 0,
            PhotoCount = Math.Max(1, visit.VisitRecordCount),
            VisitRecordCount = visit.VisitRecordCount,
            RepresentativeMediaId = visit.RepresentativeMediaId,
            AbsoluteLibraryPath = visit.AbsoluteLibraryPath,
            TopTags = visit.TopTags,
            KindLabel = kindLabel,
            DateText = dateText,
            Description = description
        };
    }

    private HeroMemoryDto? TryBuildFavoriteHero(
        IReadOnlyList<Media> favorites,
        IReadOnlyDictionary<Guid, Storage> storages,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagLookup)
    {
        var media = favorites.FirstOrDefault(item => item.PlaceId is not null);
        if (media is null)
        {
            return null;
        }

        var path = ResolvePath(media, storages);
        if (string.IsNullOrWhiteSpace(path) || media.PlaceId is not Guid placeId)
        {
            return null;
        }

        var placeName = media.Place?.DisplayName ?? string.Empty;
        var localDate = media.CapturedAt?.ToLocalTime();
        return new HeroMemoryDto
        {
            PlaceId = placeId,
            PlaceName = placeName,
            Year = localDate?.Year ?? 0,
            YearsAgo = 0,
            PhotoCount = 1,
            VisitRecordCount = 1,
            RepresentativeMediaId = media.Id,
            AbsoluteLibraryPath = path,
            TopTags = (tagLookup.GetValueOrDefault(media.Id) ?? []).Take(TopTagTake).ToList(),
            KindLabel = "추천 추억",
            DateText = localDate?.ToString("yyyy.MM.dd") ?? string.Empty,
            Description = "마음에 담아 둔 사진이에요."
        };
    }

    private IReadOnlyList<TodayMemoryPhotoDto> BuildTodayMemories(
        IReadOnlyList<Media> onThisDay,
        IReadOnlyDictionary<Guid, Storage> storages,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagLookup)
    {
        var currentYear = DateTime.Now.Year;
        return onThisDay
            .Select(media => new { Media = media, Path = ResolvePath(media, storages) })
            .Where(item => item.Path is not null)
            .OrderByDescending(item => item.Media.IsFavorite)
            .ThenByDescending(item => item.Media.CapturedAt)
            .Take(TodayPhotoTake)
            .Select(item => new TodayMemoryPhotoDto
            {
                MediaId = item.Media.Id,
                PlaceId = item.Media.PlaceId,
                PlaceName = item.Media.Place?.DisplayName ?? string.Empty,
                AbsoluteLibraryPath = item.Path!,
                YearsAgo = item.Media.CapturedAt is { } captured
                    ? Math.Max(1, currentYear - captured.Year)
                    : 1,
                TopTags = (tagLookup.GetValueOrDefault(item.Media.Id) ?? []).Take(TopTagTake).ToList()
            })
            .ToList();
    }

    private async Task<IReadOnlyList<RecentVisitDto>> BuildRecentVisitsAsync(
        MemorySearchQueryResult search,
        IReadOnlyDictionary<Guid, Storage> storages,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> tagLookup,
        CancellationToken cancellationToken)
    {
        var recent = search.Items.Take(RecentVisitTake).ToList();
        var mediaIds = recent
            .Where(item => item.RepresentativeMediaId.HasValue)
            .Select(item => item.RepresentativeMediaId!.Value)
            .Distinct()
            .ToList();

        var mediaMap = mediaIds.Count == 0
            ? new Dictionary<Guid, Media>()
            : (await _mediaRepository.GetByIdsAsync(mediaIds, cancellationToken))
                .ToDictionary(media => media.Id);

        return recent.Select(item =>
        {
            string? path = null;
            IReadOnlyList<string> tags = [];
            if (item.RepresentativeMediaId is Guid mediaId && mediaMap.TryGetValue(mediaId, out var media))
            {
                path = ResolvePath(media, storages);
                tags = tagLookup.GetValueOrDefault(mediaId) ?? [];
            }

            return new RecentVisitDto
            {
                PlaceId = item.PlaceId,
                PlaceName = item.PlaceName,
                AbsoluteLibraryPath = path,
                RepresentativeMediaId = item.RepresentativeMediaId,
                VisitRecordCount = item.VisitRecordCount,
                LastVisitDate = item.LastCapturedDate,
                TopTags = tags.Take(TopTagTake).ToList()
            };
        }).ToList();
    }

    private IReadOnlyList<DashboardPhotoDto> MapPhotos(
        IReadOnlyList<Media> mediaItems,
        IReadOnlyDictionary<Guid, Storage> storages)
    {
        return mediaItems
            .Select(media =>
            {
                var path = ResolvePath(media, storages);
                return path is null
                    ? null
                    : new DashboardPhotoDto
                    {
                        MediaId = media.Id,
                        AbsoluteLibraryPath = path,
                        IsFavorite = media.IsFavorite,
                        FileName = media.FileName
                    };
            })
            .Where(item => item is not null)
            .Cast<DashboardPhotoDto>()
            .ToList();
    }

    private async Task<PendingSummaryDto> MapPendingAsync(
        PendingBreakdownRaw pending,
        IReadOnlyDictionary<Guid, Storage> storages,
        CancellationToken cancellationToken)
    {
        string? representativePath = null;
        if (pending.RepresentativeMediaId is Guid mediaId)
        {
            var media = await _mediaRepository.GetByIdAsync(mediaId, cancellationToken);
            if (media is not null)
            {
                representativePath = ResolvePath(media, storages);
            }
        }

        return new PendingSummaryDto
        {
            Total = pending.Total,
            NoGps = pending.NoGps,
            HasGps = pending.HasGps,
            UnknownDate = pending.UnknownDate,
            RepresentativeMediaId = pending.RepresentativeMediaId,
            RepresentativeAbsoluteLibraryPath = representativePath,
            LatestImportedAt = pending.LatestImportedAt
        };
    }

    private static DashboardStatisticsDto MapStats(DashboardStatisticsRaw stats) => new()
    {
        PhotoCount = stats.PhotoCount,
        PlaceCount = stats.PlaceCount,
        VisitRecordCount = stats.VisitRecordCount,
        FavoriteCount = stats.FavoriteCount,
        TagCount = stats.TagCount
    };

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> BuildTagLookupAsync(
        IReadOnlyList<Guid> mediaIds,
        CancellationToken cancellationToken)
    {
        if (mediaIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        try
        {
            var links = await _mediaTagRepository.GetByMediaIdsAsync(mediaIds, cancellationToken);
            var tagIds = links.Select(link => link.TagId).Distinct().ToList();
            var tagNames = new Dictionary<Guid, string>();
            foreach (var tagId in tagIds)
            {
                var tag = await _tagRepository.GetByIdAsync(tagId, cancellationToken);
                if (tag is not null && tag.Source == TagSource.User)
                {
                    tagNames[tag.Id] = tag.Name;
                }
            }

            return links
                .GroupBy(link => link.MediaId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group
                        .Select(link => tagNames.GetValueOrDefault(link.TagId))
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Cast<string>()
                        .Distinct()
                        .Take(TopTagTake)
                        .ToList());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build dashboard tag lookup.");
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }
    }

    private string? ResolvePath(Media media, IReadOnlyDictionary<Guid, Storage> storages)
    {
        if (media.Storage is not null)
        {
            return _fileAccessService.ResolveAbsolutePath(media.Storage.PhotoRoot, media.RelativePath);
        }

        return storages.TryGetValue(media.StorageId, out var storage)
            ? _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, media.RelativePath)
            : null;
    }
}
