namespace MemoryKeeper.Application.Interfaces;

[Flags]
public enum CatalogSurface
{
    None = 0,
    Visits = 1,
    Pending = 2,
    Home = 4,
    Gallery = 8,
    Travel = 16,
    Favorites = 32,
    Tags = 64,
    Places = 128,
    AllRelated = Visits | Pending | Home | Gallery | Travel | Favorites,
    AllMemoryKeeper = AllRelated | Tags | Places,
}

/// <summary>
/// Marks UI catalogs dirty after media place / location changes so cached pages reload.
/// </summary>
public interface ICatalogInvalidation
{
    /// <summary>
    /// Monotonically increases for every non-empty invalidation without consuming dirty bits.
    /// </summary>
    long Generation { get; }

    /// <summary>
    /// Notifies non-UI caches while preserving the existing per-surface Consume contract.
    /// </summary>
    event EventHandler<CatalogInvalidatedEventArgs>? Invalidated;

    void Invalidate(CatalogSurface surfaces = CatalogSurface.AllRelated);

    /// <summary>
    /// Returns true if <paramref name="surface"/> was dirty, and clears that bit.
    /// </summary>
    bool Consume(CatalogSurface surface);
}

public sealed class CatalogInvalidation : ICatalogInvalidation
{
    private readonly object _gate = new();
    private CatalogSurface _dirty;
    private long _generation;

    public long Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    public event EventHandler<CatalogInvalidatedEventArgs>? Invalidated;

    public void Invalidate(CatalogSurface surfaces = CatalogSurface.AllRelated)
    {
        if (surfaces == CatalogSurface.None)
        {
            return;
        }

        long generation;
        lock (_gate)
        {
            _dirty |= surfaces;
            generation = ++_generation;
        }

        Invalidated?.Invoke(this, new CatalogInvalidatedEventArgs(generation, surfaces));
    }

    public bool Consume(CatalogSurface surface)
    {
        if (surface == CatalogSurface.None)
        {
            return false;
        }

        lock (_gate)
        {
            var wasDirty = (_dirty & surface) != 0;
            _dirty &= ~surface;
            return wasDirty;
        }
    }
}

public sealed class CatalogInvalidatedEventArgs(long generation, CatalogSurface surfaces) : EventArgs
{
    public long Generation { get; } = generation;

    public CatalogSurface Surfaces { get; } = surfaces;
}
