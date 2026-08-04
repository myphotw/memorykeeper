using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Time;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class PendingMemoryService
{
    private readonly IMediaRepository _mediaRepository;
    private readonly IStorageRepository _storageRepository;
    private readonly IFileAccessService _fileAccessService;
    private readonly ILocationResolver _locationResolver;
    private readonly MemoryGroupingService _memoryGroupingService;
    private readonly MediaPlaceAssignmentService _mediaPlaceAssignmentService;
    private readonly ILogger<PendingMemoryService> _logger;

    public PendingMemoryService(
        IMediaRepository mediaRepository,
        IStorageRepository storageRepository,
        IFileAccessService fileAccessService,
        ILocationResolver locationResolver,
        MemoryGroupingService memoryGroupingService,
        MediaPlaceAssignmentService mediaPlaceAssignmentService,
        ILogger<PendingMemoryService> logger)
    {
        _mediaRepository = mediaRepository;
        _storageRepository = storageRepository;
        _fileAccessService = fileAccessService;
        _locationResolver = locationResolver;
        _memoryGroupingService = memoryGroupingService;
        _mediaPlaceAssignmentService = mediaPlaceAssignmentService;
        _logger = logger;
    }

    public async Task<PendingMemoryOverviewDto> GetPendingMemoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var unassigned = await _mediaRepository.GetUnassignedAsync(cancellationToken);
        var storages = (await _storageRepository.GetAllAsync(cancellationToken))
            .ToDictionary(storage => storage.Id);

        var reclassificationCandidates = unassigned
            .Where(MemoryGroupingService.HasGps)
            .Where(media => media.MediaType == MediaType.Photo)
            .OrderByDescending(media => media.CapturedAt)
            .ThenByDescending(media => media.ImportedAt)
            .Select(media => MapItem(media, storages))
            .Where(item => item is not null)
            .Cast<PendingMemoryItemDto>()
            .ToList();

        var groups = new List<PendingMemoryGroupDto>();
        foreach (var mediaGroup in _memoryGroupingService.GroupWithoutGps(unassigned))
        {
            groups.Add(await MapGroupAsync(mediaGroup, storages, cancellationToken));
        }

        _logger.LogInformation(
            "Pending memories calculated. GroupCount={GroupCount}, ReclassificationCount={ReclassificationCount}",
            groups.Count,
            reclassificationCandidates.Count);

        return new PendingMemoryOverviewDto
        {
            Groups = groups,
            ReclassificationCandidates = reclassificationCandidates
        };
    }

    public Task<AssignMediaPlaceResult> AssignPlaceAsync(
        AssignMediaPlaceRequest request,
        CancellationToken cancellationToken = default)
    {
        return _mediaPlaceAssignmentService.AssignAsync(request, cancellationToken);
    }

    private async Task<PendingMemoryGroupDto> MapGroupAsync(
        IReadOnlyList<Media> mediaGroup,
        IReadOnlyDictionary<Guid, Domain.Entities.Storage> storages,
        CancellationToken cancellationToken)
    {
        var items = mediaGroup
            .Select(media => MapItem(media, storages))
            .Where(item => item is not null)
            .Cast<PendingMemoryItemDto>()
            .ToList();

        var hasUnknownDate = MemoryGroupingService.GroupHasUnknownDate(mediaGroup);
        var capturedDates = mediaGroup
            .Select(MemoryGroupingService.GetCapturedAt)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .OrderBy(value => value)
            .ToList();

        var estimated = await EstimateLocationAsync(mediaGroup, cancellationToken);

        return new PendingMemoryGroupDto
        {
            GroupId = MemoryGroupingService.CreateTemporaryGroupId(mediaGroup),
            GroupName = MemoryGroupingService.BuildGroupName(mediaGroup),
            MediaCount = items.Count,
            HasUnknownDate = hasUnknownDate,
            FirstCapturedDate = hasUnknownDate || capturedDates.Count == 0 ? null : capturedDates[0],
            LastCapturedDate = hasUnknownDate || capturedDates.Count == 0 ? null : capturedDates[^1],
            EstimatedCountry = estimated.Country,
            EstimatedCity = estimated.City,
            EstimatedAddress = estimated.Address,
            EstimatedLocationSummary = estimated.Summary,
            ProcessingStatus = "미처리",
            MediaItems = items
        };
    }

    private async Task<(string Country, string City, string Address, string Summary)> EstimateLocationAsync(
        IReadOnlyList<Media> mediaGroup,
        CancellationToken cancellationToken)
    {
        var gpsMedia = mediaGroup.FirstOrDefault(MemoryGroupingService.HasGps);
        if (gpsMedia is null)
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }

        try
        {
            var location = await _locationResolver.ResolveAsync(
                gpsMedia.Latitude!.Value,
                gpsMedia.Longitude!.Value,
                cancellationToken);

            if (location is null)
            {
                return (string.Empty, string.Empty, string.Empty, string.Empty);
            }

            var summary = string.Join(
                " ",
                new[] { location.Country, location.City }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            return (location.Country, location.City, location.Address, summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to estimate pending group location. MediaId={MediaId}",
                gpsMedia.Id);
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }

    private PendingMemoryItemDto? MapItem(
        Media media,
        IReadOnlyDictionary<Guid, Domain.Entities.Storage> storages)
    {
        if (!storages.TryGetValue(media.StorageId, out var storage))
        {
            return null;
        }

        return new PendingMemoryItemDto
        {
            MediaId = media.Id,
            FileName = media.FileName,
            AbsoluteLibraryPath = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, media.RelativePath),
            CapturedAt = DateTimeHelper.ToUtcOffset(media.CapturedAt),
            Latitude = media.Latitude,
            Longitude = media.Longitude
        };
    }
}
