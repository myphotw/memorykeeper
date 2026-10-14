using System.Buffers;
using System.Security.Cryptography;

namespace MemoryKeeper.Mobile.Uploads;

public interface IMobileContentHasher
{
    Task<MobileContentHashResult> ComputeSha256Async(
        MobileSelectedMediaFile file,
        CancellationToken cancellationToken = default);
}

public sealed class MobileContentHasher : IMobileContentHasher
{
    internal const int BufferSize = 64 * 1024;

    public async Task<MobileContentHashResult> ComputeSha256Async(
        MobileSelectedMediaFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        try
        {
            await using var stream = await file.OpenReadAsync(cancellationToken).ConfigureAwait(false);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                long bytesRead = 0;
                int read;
                while ((read = await stream
                           .ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    bytesRead += read;
                }

                var sha256 = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                return new MobileContentHashResult(sha256, bytesRead);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MobileUploadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MobileUploadException(
                MobileUploadErrorCodes.FileAccess,
                "선택한 파일을 읽을 수 없습니다. 파일을 다시 선택해 주세요.",
                innerException: ex);
        }
    }
}
