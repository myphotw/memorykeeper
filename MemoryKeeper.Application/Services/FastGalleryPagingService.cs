using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// UI-independent cursor paging state for the Fast Gallery feed.
/// A failed first-page refresh never replaces the last successful snapshot.
/// </summary>
public sealed class FastGalleryPagingService
{
    public const int PageSize = 50;

    private readonly IFastGalleryApiRepository _repository;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly List<FastGalleryPhotoDto> _items = [];
    private readonly HashSet<string> _fileIds = new(StringComparer.Ordinal);

    public FastGalleryPagingService(IFastGalleryApiRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public IReadOnlyList<FastGalleryPhotoDto> Items => _items;

    public string? NextCursor { get; private set; }

    public bool HasMore { get; private set; } = true;

    public async Task<FastGalleryPagingUpdate> LoadFirstPageAsync(
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var page = await _repository.GetPhotosAsync(
                    new FastGalleryPhotoQuery { Limit = PageSize },
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var uniqueItems = Deduplicate(page.Items);
            var (nextCursor, hasMore) = ResolveContinuation(page, requestedCursor: null);

            _items.Clear();
            _items.AddRange(uniqueItems);
            _fileIds.Clear();
            foreach (var item in uniqueItems)
            {
                _fileIds.Add(item.FileId);
            }

            NextCursor = nextCursor;
            HasMore = hasMore;
            return Snapshot(uniqueItems, applied: true, skipped: false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<FastGalleryPagingUpdate> LoadNextPageAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return Snapshot([], applied: false, skipped: true);
        }

        try
        {
            if (!HasMore || string.IsNullOrWhiteSpace(NextCursor))
            {
                return Snapshot([], applied: false, skipped: true);
            }

            var requestedCursor = NextCursor;
            var page = await _repository.GetPhotosAsync(
                    new FastGalleryPhotoQuery
                    {
                        Limit = PageSize,
                        Cursor = requestedCursor,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var addedItems = new List<FastGalleryPhotoDto>();
            foreach (var item in page.Items)
            {
                if (string.IsNullOrWhiteSpace(item.FileId) || !_fileIds.Add(item.FileId))
                {
                    continue;
                }

                _items.Add(item);
                addedItems.Add(item);
            }

            (NextCursor, HasMore) = ResolveContinuation(page, requestedCursor);
            return Snapshot(addedItems, applied: true, skipped: false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static IReadOnlyList<FastGalleryPhotoDto> Deduplicate(
        IEnumerable<FastGalleryPhotoDto> source)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return source
            .Where(item => !string.IsNullOrWhiteSpace(item.FileId) && seen.Add(item.FileId))
            .ToArray();
    }

    private static (string? NextCursor, bool HasMore) ResolveContinuation(
        FastGalleryPhotoPageDto page,
        string? requestedCursor)
    {
        var cursor = string.IsNullOrWhiteSpace(page.NextCursor) ? null : page.NextCursor;
        var hasMore = page.HasMore
                      && cursor is not null
                      && !string.Equals(cursor, requestedCursor, StringComparison.Ordinal);
        return hasMore ? (cursor, true) : (null, false);
    }

    private FastGalleryPagingUpdate Snapshot(
        IReadOnlyList<FastGalleryPhotoDto> addedItems,
        bool applied,
        bool skipped) =>
        new(
            _items.ToArray(),
            addedItems,
            NextCursor,
            HasMore,
            applied,
            skipped);
}

public sealed record FastGalleryPagingUpdate(
    IReadOnlyList<FastGalleryPhotoDto> Items,
    IReadOnlyList<FastGalleryPhotoDto> AddedItems,
    string? NextCursor,
    bool HasMore,
    bool Applied,
    bool Skipped);
