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
    private FastGalleryPhotoQuery _baseQuery = new() { Limit = PageSize };
    private List<CompositeQueryState>? _compositeQueries;

    public FastGalleryPagingService(IFastGalleryApiRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public IReadOnlyList<FastGalleryPhotoDto> Items => _items;

    public string? NextCursor { get; private set; }

    public bool HasMore { get; private set; } = true;

    public async Task<FastGalleryPagingUpdate> LoadFirstPageAsync(
        CancellationToken cancellationToken = default)
        => await LoadFirstPageAsync(
                new FastGalleryPhotoQuery { Limit = PageSize },
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<FastGalleryPagingUpdate> LoadFirstPageAsync(
        FastGalleryPhotoQuery baseQuery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseQuery);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requestedBaseQuery = WithPaging(baseQuery, cursor: null);
            var page = await _repository.GetPhotosAsync(
                    requestedBaseQuery,
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
            _baseQuery = requestedBaseQuery;
            _compositeQueries = null;
            return Snapshot(uniqueItems, applied: true, skipped: false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<FastGalleryPagingUpdate> LoadFirstPageAsync(
        IReadOnlyList<FastGalleryPhotoQuery> baseQueries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseQueries);
        if (baseQueries.Count == 0)
        {
            throw new ArgumentException("At least one Gallery query is required.", nameof(baseQueries));
        }

        if (baseQueries.Count == 1)
        {
            return await LoadFirstPageAsync(baseQueries[0], cancellationToken).ConfigureAwait(false);
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requestedQueries = baseQueries
                .Select(query => WithPaging(query, cursor: null))
                .ToArray();
            var pages = await Task.WhenAll(requestedQueries.Select(query =>
                    _repository.GetPhotosAsync(query, cancellationToken)))
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var states = requestedQueries
                .Zip(pages, (query, page) => CompositeQueryState.FromFirstPage(query, page))
                .ToList();
            var firstPage = await TakeCompositePageAsync(states, [], cancellationToken)
                .ConfigureAwait(false);

            _items.Clear();
            _items.AddRange(firstPage);
            _fileIds.Clear();
            foreach (var item in firstPage)
            {
                _fileIds.Add(item.FileId);
            }

            _compositeQueries = states;
            ApplyCompositeContinuation(states);
            return Snapshot(firstPage, applied: true, skipped: false);
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

            if (_compositeQueries is not null)
            {
                var workingStates = _compositeQueries.Select(state => state.Clone()).ToList();
                var compositeAddedItems = await TakeCompositePageAsync(
                        workingStates,
                        _fileIds,
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                _items.AddRange(compositeAddedItems);
                foreach (var item in compositeAddedItems)
                {
                    _fileIds.Add(item.FileId);
                }

                _compositeQueries = workingStates;
                ApplyCompositeContinuation(workingStates);
                return Snapshot(compositeAddedItems, applied: true, skipped: false);
            }

            var requestedCursor = NextCursor;
            var page = await _repository.GetPhotosAsync(
                    WithPaging(_baseQuery, requestedCursor),
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

    private async Task<IReadOnlyList<FastGalleryPhotoDto>> TakeCompositePageAsync(
        IReadOnlyList<CompositeQueryState> states,
        IEnumerable<string> existingFileIds,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(existingFileIds, StringComparer.Ordinal);
        var result = new List<FastGalleryPhotoDto>(PageSize);
        while (result.Count < PageSize)
        {
            await EnsureCompositeBuffersAsync(states, cancellationToken).ConfigureAwait(false);
            var nextState = states
                .Where(state => state.Buffer.Count > 0)
                .OrderByDescending(state => state.Buffer.Peek().EffectiveCaptureDatetime.HasValue)
                .ThenByDescending(state => state.Buffer.Peek().EffectiveCaptureDatetime)
                .ThenByDescending(state => state.Buffer.Peek().EffectiveCaptureYear)
                .ThenBy(state => state.Buffer.Peek().FileId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (nextState is null)
            {
                break;
            }

            var item = nextState.Buffer.Dequeue();
            if (!string.IsNullOrWhiteSpace(item.FileId) && seen.Add(item.FileId))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private async Task EnsureCompositeBuffersAsync(
        IReadOnlyList<CompositeQueryState> states,
        CancellationToken cancellationToken)
    {
        foreach (var state in states.Where(state => state.Buffer.Count == 0 && state.HasMore))
        {
            var requestedCursor = state.NextCursor;
            var page = await _repository.GetPhotosAsync(
                    WithPaging(state.BaseQuery, requestedCursor),
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            state.Append(page, requestedCursor);
        }
    }

    private void ApplyCompositeContinuation(IReadOnlyList<CompositeQueryState> states)
    {
        HasMore = states.Any(state => state.Buffer.Count > 0 || state.HasMore);
        NextCursor = HasMore ? "composite" : null;
    }

    private static IReadOnlyList<FastGalleryPhotoDto> Deduplicate(
        IEnumerable<FastGalleryPhotoDto> source)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return source
            .Where(item => !string.IsNullOrWhiteSpace(item.FileId) && seen.Add(item.FileId))
            .ToArray();
    }

    private static FastGalleryPhotoQuery WithPaging(
        FastGalleryPhotoQuery query,
        string? cursor) => new()
    {
        Limit = PageSize,
        Cursor = cursor,
        Year = query.Year,
        Country = query.Country,
        Region = query.Region,
        LocationKey = query.LocationKey,
        PlaceId = query.PlaceId,
        Unclassified = query.Unclassified,
        DateUnclassified = query.DateUnclassified,
        Favorite = query.Favorite,
        HasGps = query.HasGps,
        DateFrom = query.DateFrom,
        DateTo = query.DateTo,
        PhotoCategory = query.PhotoCategory,
    };

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

    private sealed class CompositeQueryState
    {
        private CompositeQueryState(FastGalleryPhotoQuery baseQuery)
        {
            BaseQuery = baseQuery;
        }

        public FastGalleryPhotoQuery BaseQuery { get; }

        public Queue<FastGalleryPhotoDto> Buffer { get; } = new();

        public string? NextCursor { get; private set; }

        public bool HasMore { get; private set; }

        public static CompositeQueryState FromFirstPage(
            FastGalleryPhotoQuery query,
            FastGalleryPhotoPageDto page)
        {
            var state = new CompositeQueryState(query);
            state.Append(page, requestedCursor: null);
            return state;
        }

        public CompositeQueryState Clone()
        {
            var clone = new CompositeQueryState(BaseQuery)
            {
                NextCursor = NextCursor,
                HasMore = HasMore,
            };
            foreach (var item in Buffer)
            {
                clone.Buffer.Enqueue(item);
            }

            return clone;
        }

        public void Append(FastGalleryPhotoPageDto page, string? requestedCursor)
        {
            foreach (var item in page.Items)
            {
                Buffer.Enqueue(item);
            }

            (NextCursor, HasMore) = ResolveContinuation(page, requestedCursor);
        }
    }
}

public sealed record FastGalleryPagingUpdate(
    IReadOnlyList<FastGalleryPhotoDto> Items,
    IReadOnlyList<FastGalleryPhotoDto> AddedItems,
    string? NextCursor,
    bool HasMore,
    bool Applied,
    bool Skipped);
