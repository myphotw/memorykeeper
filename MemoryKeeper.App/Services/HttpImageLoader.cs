using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using MemoryKeeper.Infrastructure.Services.Api;
using Windows.Storage.Streams;

namespace MemoryKeeper.App.Services;

/// <summary>
/// Creates WinUI <see cref="BitmapImage"/> instances from authenticated HTTP(S) media bytes.
/// </summary>
public static class HttpImageLoader
{
    private static BackendMediaDownloader? _downloader;

    public static void Configure(BackendMediaDownloader downloader) =>
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));

    public static bool IsHttpUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    public static BitmapImage? TryCreate(
        string? absoluteUrl,
        ILogger? logger = null,
        string? context = null)
    {
        if (!IsHttpUrl(absoluteUrl))
        {
            logger?.LogWarning(
                "HttpImageLoader skipped non-HTTP URL. Context={Context}",
                context);
            return null;
        }

        var downloader = _downloader;
        if (downloader is null)
        {
            logger?.LogWarning(
                "HttpImageLoader is not configured. Context={Context}",
                context);
            return null;
        }

        try
        {
            var bitmap = new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.None,
            };

            bitmap.ImageOpened += (_, _) =>
            {
                logger?.LogInformation(
                    "HttpImageLoader ImageOpened. Context={Context}",
                    context);
            };
            bitmap.ImageFailed += (_, args) =>
            {
                logger?.LogWarning(
                    "HttpImageLoader ImageFailed. Context={Context}, Error={Error}",
                    context,
                    args.ErrorMessage);
            };

            _ = LoadAuthenticatedAsync(bitmap, downloader, absoluteUrl!, logger, context);
            return bitmap;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "HttpImageLoader creation failed. Context={Context}",
                context);
            return null;
        }
    }

    private static async Task LoadAuthenticatedAsync(
        BitmapImage bitmap,
        BackendMediaDownloader downloader,
        string url,
        ILogger? logger,
        string? context)
    {
        try
        {
            var bytes = await downloader.GetBytesAsync(url);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            logger?.LogInformation(
                "HttpImageLoader authenticated source loaded. Context={Context}",
                context);
        }
        catch (ApiException ex)
        {
            logger?.LogWarning(
                "HttpImageLoader request failed. Context={Context}, Category={Category}, StatusCode={StatusCode}",
                context,
                ex.Category,
                (int)ex.StatusCode);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "HttpImageLoader decode failed. Context={Context}", context);
        }
    }
}
