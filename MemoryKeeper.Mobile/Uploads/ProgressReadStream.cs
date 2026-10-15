namespace MemoryKeeper.Mobile.Uploads;

internal sealed class ProgressReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _totalBytes;
    private readonly IProgress<MobileUploadProgress> _progress;
    private long _bytesRead;

    public ProgressReadStream(
        Stream inner,
        long totalBytes,
        IProgress<MobileUploadProgress> progress)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _totalBytes = Math.Max(0, totalBytes);
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Report(read);
        return read;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var read = await _inner
            .ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
            .ConfigureAwait(false);
        Report(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Report(read);
        return read;
    }

    private void Report(int read)
    {
        if (read <= 0)
        {
            return;
        }

        var sent = Interlocked.Add(ref _bytesRead, read);
        _progress.Report(new MobileUploadProgress(sent, _totalBytes));
    }

    protected override void Dispose(bool disposing)
    {
        // The owner of the selected-file stream disposes it after the request finishes.
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
