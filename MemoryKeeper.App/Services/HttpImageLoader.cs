using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.Services;

/// <summary>
/// Creates WinUI <see cref="BitmapImage"/> instances from absolute HTTP(S) URLs.
/// </summary>
public static class HttpImageLoader
{
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
                "HttpImageLoader skipped non-HTTP URL. Context={Context}, Url={Url}",
                context,
                absoluteUrl);
            return null;
        }

        try
        {
            var uri = new Uri(absoluteUrl!, UriKind.Absolute);
            var bitmap = new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.None,
            };

            bitmap.ImageOpened += (_, _) =>
            {
                logger?.LogInformation(
                    "HttpImageLoader ImageOpened. Context={Context}, Url={Url}",
                    context,
                    absoluteUrl);
            };
            bitmap.ImageFailed += (_, args) =>
            {
                logger?.LogWarning(
                    "HttpImageLoader ImageFailed. Context={Context}, Url={Url}, Error={Error}",
                    context,
                    absoluteUrl,
                    args.ErrorMessage);
            };

            bitmap.UriSource = uri;
            logger?.LogInformation(
                "HttpImageLoader UriSource set. Context={Context}, Url={Url}",
                context,
                absoluteUrl);
            return bitmap;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "HttpImageLoader Uri create failed. Context={Context}, Url={Url}",
                context,
                absoluteUrl);
            return null;
        }
    }
}
