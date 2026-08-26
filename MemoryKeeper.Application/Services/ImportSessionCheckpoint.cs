using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>Coalesces many accepted-job updates into bounded, safe full-session checkpoints.</summary>
public sealed class ImportSessionCheckpoint : IAsyncDisposable
{
    private readonly IImportJobSessionStore _store;
    private readonly Func<IReadOnlyList<ImportSessionJobDto>> _snapshot;
    private readonly Func<IReadOnlyCollection<string>> _managedJobIds;
    private readonly int _batchSize;
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly object _gate = new();
    private int _dirtyCount;
    private Task? _scheduledFlush;
    private bool _disposed;
    private readonly CancellationTokenSource _scheduleCancellation = new();

    public bool HasWarning { get; private set; }

    public ImportSessionCheckpoint(
        IImportJobSessionStore store,
        Func<IReadOnlyList<ImportSessionJobDto>> snapshot,
        Func<IReadOnlyCollection<string>> managedJobIds,
        int batchSize,
        TimeSpan interval,
        ILogger logger)
    {
        _store = store;
        _snapshot = snapshot;
        _managedJobIds = managedJobIds;
        _batchSize = Math.Max(1, batchSize);
        _interval = interval;
        _logger = logger;
    }

    public Task RequestAsync()
    {
        var flushNow = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            _dirtyCount++;
            flushNow = _dirtyCount >= _batchSize;
            if (!flushNow && (_scheduledFlush is null || _scheduledFlush.IsCompleted))
            {
                _scheduledFlush = FlushAfterDelayAsync();
            }
        }

        return flushNow ? FlushAsync() : Task.CompletedTask;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_dirtyCount == 0)
                {
                    return;
                }

                _dirtyCount = 0;
            }

            var jobs = _snapshot();
            await _store.UpdateAsync(jobs, _managedJobIds(), cancellationToken).ConfigureAwait(false);

            HasWarning = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate)
            {
                _dirtyCount = Math.Max(1, _dirtyCount);
            }

            _logger.LogWarning(ex, "Import session checkpoint failed; accepted Backend jobs remain active.");
            PhotoRegisterLog.WriteWarning("import-jobs-session.json", "SESSION_CHECKPOINT", ex);
            HasWarning = true;
        }
        finally
        {
            _flushGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _scheduleCancellation.Cancel();
        if (_scheduledFlush is not null)
        {
            await _scheduledFlush.ConfigureAwait(false);
        }
        await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        _flushGate.Dispose();
        _scheduleCancellation.Dispose();
    }

    private async Task FlushAfterDelayAsync()
    {
        try
        {
            await Task.Delay(_interval, _scheduleCancellation.Token).ConfigureAwait(false);
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_scheduleCancellation.IsCancellationRequested)
        {
            // Final flush is performed by DisposeAsync.
        }
    }
}
