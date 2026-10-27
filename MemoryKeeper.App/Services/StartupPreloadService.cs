using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.App.Services;

public sealed class StartupPreloadService
{
    private readonly IFastGalleryApiRepository _gallery;
    private readonly IMemoryKeeperPlaceApiRepository _places;
    private readonly object _gate = new();
    private Task<StartupPreloadResult>? _inFlight;

    public StartupPreloadService(
        IFastGalleryApiRepository gallery,
        IMemoryKeeperPlaceApiRepository places)
    {
        _gallery = gallery;
        _places = places;
    }

    public Task<StartupPreloadResult> EnsureRequiredAsync()
    {
        lock (_gate)
        {
            if (_inFlight is { IsCompleted: false })
            {
                return _inFlight;
            }

            _inFlight = PreloadAsync();
            return _inFlight;
        }
    }

    private async Task<StartupPreloadResult> PreloadAsync()
    {
        var required = new[]
        {
            TryWarmAsync(() => _gallery.GetPhotosAsync(new FastGalleryPhotoQuery { Limit = 50 })),
            TryWarmAsync(() => _gallery.GetSummaryAsync()),
            TryWarmAsync(() => _gallery.GetHierarchyAsync()),
            TryWarmAsync(() => _places.GetPlacesAsync()),
            TryWarmAsync(() => _places.GetPlaceHierarchyAsync()),
        };

        // Duplicate suggestions are useful in Place Management, but not required to
        // release the startup overlay. The shared cache makes a later menu request join it.
        _ = TryWarmAsync(() => _places.GetDuplicateCandidatesAsync());

        var results = await Task.WhenAll(required).ConfigureAwait(false);
        return new StartupPreloadResult(results.Count(succeeded => succeeded), results.Count(succeeded => !succeeded));
    }

    private static async Task<bool> TryWarmAsync(Func<Task> load)
    {
        try
        {
            await load().ConfigureAwait(false);
            return true;
        }
        catch
        {
            // Fail open: the target menu keeps its normal error and retry behavior.
            return false;
        }
    }
}

public sealed record StartupPreloadResult(int SucceededCount, int FailedCount)
{
    public bool Succeeded => FailedCount == 0;
}
