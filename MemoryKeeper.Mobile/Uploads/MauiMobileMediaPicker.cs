using System.Buffers;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace MemoryKeeper.Mobile.Uploads;

public sealed class MauiMobileMediaPicker : IMobileMediaPicker
{
    private const int BufferSize = 64 * 1024;

    private static readonly FilePickerFileType PhotoAndVideoTypes = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.Android] = ["image/*", "video/*"],
        });

    public async Task<IReadOnlyList<MobileSelectedMediaFile>> PickMultipleAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<FileResult>? results;
        try
        {
            results = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "업로드할 사진과 동영상 선택",
                FileTypes = PhotoAndVideoTypes,
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MobileUploadException(
                MobileUploadErrorCodes.FileAccess,
                "사진 또는 동영상을 선택할 수 없습니다.",
                innerException: ex);
        }

        var selected = new List<MobileSelectedMediaFile>();
        foreach (var result in results ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = await MeasureAsync(result, cancellationToken).ConfigureAwait(false);
            selected.Add(new MobileSelectedMediaFile(
                result.FileName,
                ResolveContentType(result),
                size,
                token => OpenReadAsync(result, token)));
        }

        return selected;
    }

    private static async Task<Stream> OpenReadAsync(
        FileResult result,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var stream = await result.OpenReadAsync().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            return stream;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MobileUploadException(
                MobileUploadErrorCodes.FileAccess,
                "선택한 파일을 열 수 없습니다. 파일을 다시 선택해 주세요.",
                innerException: ex);
        }
    }

    private static async Task<long> MeasureAsync(
        FileResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await OpenReadAsync(result, cancellationToken).ConfigureAwait(false);
            if (stream.CanSeek)
            {
                try
                {
                    return stream.Length;
                }
                catch (NotSupportedException)
                {
                    // Some Android content providers expose a seekable stream without Length.
                }
            }

            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                long length = 0;
                int read;
                while ((read = await stream
                           .ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    length += read;
                }

                return length;
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
                "선택한 파일의 크기를 확인할 수 없습니다. 파일을 다시 선택해 주세요.",
                innerException: ex);
        }
    }

    private static string ResolveContentType(FileResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ContentType))
        {
            return result.ContentType;
        }

        return Path.GetExtension(result.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".heic" or ".heif" => "image/heic",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            _ => "application/octet-stream",
        };
    }
}
