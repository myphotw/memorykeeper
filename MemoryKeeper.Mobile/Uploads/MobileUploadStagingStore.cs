using System.Buffers;

namespace MemoryKeeper.Mobile.Uploads;

public sealed class MobileUploadStagingStore : IMobileUploadStagingStore
{
    internal const int BufferSize = 64 * 1024;
    internal static readonly TimeSpan StaleFileAge = TimeSpan.FromDays(1);

    private readonly string _rootDirectory;
    private int _staleCleanupStarted;

    public MobileUploadStagingStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public async Task<MobileSelectedMediaFile> StageAsync(
        string fileName,
        string contentType,
        Stream originalStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(originalStream);
        if (!originalStream.CanRead)
        {
            throw new MobileUploadException(
                MobileUploadErrorCodes.OriginalMediaUnavailable,
                "선택한 파일의 원본 데이터를 읽을 수 없습니다.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_rootDirectory);
        CleanupStaleFilesOnce();

        var stagedPath = Path.Combine(_rootDirectory, $"{Guid.NewGuid():N}.upload");
        try
        {
            await using (var destination = new FileStream(
                             stagedPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                try
                {
                    int read;
                    while ((read = await originalStream
                               .ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                               .ConfigureAwait(false)) > 0)
                    {
                        await destination
                            .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                            .ConfigureAwait(false);
                    }

                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            var stagedSize = new FileInfo(stagedPath).Length;
            return new MobileSelectedMediaFile(
                fileName,
                contentType,
                stagedSize,
                cancellation => OpenStagedReadAsync(stagedPath, cancellation),
                () => DeleteStagedFileAsync(stagedPath));
        }
        catch
        {
            TryDelete(stagedPath);
            throw;
        }
    }

    private static Task<Stream> OpenStagedReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    private static ValueTask DeleteStagedFileAsync(string path)
    {
        TryDelete(path);
        return ValueTask.CompletedTask;
    }

    private void CleanupStaleFilesOnce()
    {
        if (Interlocked.Exchange(ref _staleCleanupStarted, 1) != 0)
        {
            return;
        }

        var cutoff = DateTime.UtcNow - StaleFileAge;
        try
        {
            foreach (var path in Directory.EnumerateFiles(_rootDirectory, "*.upload"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff)
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
