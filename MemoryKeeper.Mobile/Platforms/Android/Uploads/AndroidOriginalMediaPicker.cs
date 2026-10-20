using Android.Content;
using Android.Provider;
using MemoryKeeper.Mobile.Uploads;
using Microsoft.Maui.ApplicationModel;
using System.Runtime.Versioning;
using AndroidUri = Android.Net.Uri;

namespace MemoryKeeper.Mobile.Platforms.Android.Uploads;

public sealed class AndroidOriginalMediaPicker : IMobileMediaPicker
{
    private const string DiagnosticTag = "MemoryKeeper.Upload";
    private const string MediaDataColumn = "_data";
    private readonly IMobileUploadStagingStore _stagingStore;

    public AndroidOriginalMediaPicker(IMobileUploadStagingStore stagingStore)
    {
        _stagingStore = stagingStore ?? throw new ArgumentNullException(nameof(stagingStore));
    }

    public async Task<IReadOnlyList<MobileSelectedMediaFile>> PickMultipleAsync(
        CancellationToken cancellationToken = default)
    {
        var activity = Platform.CurrentActivity as MainActivity
                       ?? throw new MobileUploadException(
                           MobileUploadErrorCodes.FileAccess,
                           "사진 선택 화면을 열 수 없습니다. 앱 화면으로 돌아온 뒤 다시 시도해 주세요.");
        AndroidUploadMediaPickerResult? pickerResult;
        try
        {
            pickerResult = await activity.PickUploadMediaAsync(cancellationToken);
        }
        catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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

        if (pickerResult is null)
        {
            return [];
        }

        using (pickerResult.Intent)
        {
            var resolver = activity.ContentResolver
                           ?? throw new MobileUploadException(
                               MobileUploadErrorCodes.FileAccess,
                               "사진 또는 동영상 데이터를 읽을 수 없습니다.");
            var selections = ExtractUris(pickerResult.Intent)
                .Select(uri => Describe(activity, resolver, uri))
                .ToArray();
            global::Android.Util.Log.Info(
                DiagnosticTag,
                $"MK_UPLOAD_ORIGINAL event=selection picker={PickerName(pickerResult)} selected_count={selections.Length}");
            var supportsOriginalPhotoAccess = OperatingSystem.IsAndroidVersionAtLeast(
                AndroidOriginalMediaAccessPolicy.Android10ApiLevel);
            var needsOriginalPhotoAccess = supportsOriginalPhotoAccess
                                           && selections.Any(selection => selection.IsPhoto);
            var hasOriginalPhotoPermission = !needsOriginalPhotoAccess
                                             || await activity.RequestMediaLocationPermissionAsync(
                                                 cancellationToken);
            if (needsOriginalPhotoAccess && !hasOriginalPhotoPermission)
            {
                global::Android.Util.Log.Info(
                    DiagnosticTag,
                    $"MK_UPLOAD_ORIGINAL event=permission_denied selected_count={selections.Length}");
            }

            var staged = new List<MobileSelectedMediaFile>(selections.Length);
            var permissionDeniedCount = 0;
            var unsupportedProviderCount = 0;
            var readFailureCount = 0;
            try
            {
                foreach (var selection in selections)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AndroidUri? mediaStoreUri = null;
                    if (selection.IsPhoto)
                    {
                        if (OperatingSystem.IsAndroidVersionAtLeast(29))
                        {
                            mediaStoreUri = ResolveMediaStoreUri(activity, selection.Uri);
                        }
                    }
                    var readMode = AndroidOriginalMediaAccessPolicy.Decide(
                        supportsOriginalPhotoAccess
                            ? AndroidOriginalMediaAccessPolicy.Android10ApiLevel
                            : AndroidOriginalMediaAccessPolicy.Android10ApiLevel - 1,
                        selection.IsPhoto,
                        hasOriginalPhotoPermission,
                        pickerResult.UsedLocationAwarePhotoPicker,
                        mediaStoreUri is not null);
                    if (readMode == AndroidOriginalMediaReadMode.PermissionRequired)
                    {
                        permissionDeniedCount++;
                        staged.Add(Unavailable(
                            selection,
                            MobileUploadErrorCodes.OriginalMediaPermissionRequired,
                            "사진의 GPS를 포함한 원본을 보관하려면 사진 위치정보 접근을 허용해 주세요."));
                        continue;
                    }

                    var readUri = selection.Uri;
                    if (readMode == AndroidOriginalMediaReadMode.UnsupportedProvider)
                    {
                        unsupportedProviderCount++;
                        staged.Add(Unavailable(
                            selection,
                            MobileUploadErrorCodes.OriginalMediaUnavailable,
                            "이 사진 제공자는 GPS를 포함한 원본 전송을 지원하지 않습니다. 기기에 원본을 저장한 뒤 다시 선택해 주세요."));
                        continue;
                    }

                    try
                    {
                        if (readMode == AndroidOriginalMediaReadMode.RequireMediaStoreOriginal)
                        {
                            if (mediaStoreUri is null)
                            {
                                throw new IOException(
                                    "The provider did not expose an Android 10 original media URI.");
                            }

                            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                            {
                                readUri = RequireOriginalUri(mediaStoreUri)
                                          ?? throw new IOException(
                                              "The provider returned no original media URI.");
                            }
                            else
                            {
                                throw new IOException(
                                    "Android 10 original media access is unavailable.");
                            }
                        }

                        await using var originalStream = resolver.OpenInputStream(readUri)
                                                           ?? throw new IOException(
                                                               "The provider returned no original stream.");
                        staged.Add(await _stagingStore.StageAsync(
                            selection.FileName,
                            selection.ContentType,
                            originalStream,
                            cancellationToken));
                    }
                    catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        readFailureCount++;
                        global::Android.Util.Log.Warn(
                            DiagnosticTag,
                            $"MK_UPLOAD_ORIGINAL event=read_failed media_type={(selection.IsPhoto ? "photo" : "video")} picker={PickerName(pickerResult)} error_type={ex.GetType().Name}");
                        staged.Add(Unavailable(
                            selection,
                            MobileUploadErrorCodes.OriginalMediaUnavailable,
                            selection.IsPhoto
                                ? "사진의 GPS를 포함한 원본을 읽을 수 없습니다. 기기에 원본을 저장한 뒤 다시 선택해 주세요."
                                : "선택한 동영상 원본을 읽을 수 없습니다. 파일을 다시 선택해 주세요."));
                    }
                }

                global::Android.Util.Log.Info(
                    DiagnosticTag,
                    $"MK_UPLOAD_ORIGINAL event=staging_complete selected_count={selections.Length} staged_count={staged.Count - permissionDeniedCount - unsupportedProviderCount - readFailureCount} permission_denied_count={permissionDeniedCount} unsupported_provider_count={unsupportedProviderCount} read_failure_count={readFailureCount}");
                return staged;
            }
            catch
            {
                foreach (var file in staged)
                {
                    await file.ReleaseAsync();
                }

                throw;
            }
        }
    }

    private static string PickerName(AndroidUploadMediaPickerResult pickerResult) =>
        pickerResult.UsedLocationAwarePhotoPicker ? "photo_picker" : "get_content";

    private static IReadOnlyList<AndroidUri> ExtractUris(Intent resultIntent)
    {
        var uris = new List<AndroidUri>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (resultIntent.ClipData is { } clipData)
        {
            for (var index = 0; index < clipData.ItemCount; index++)
            {
                AddUri(clipData.GetItemAt(index)?.Uri);
            }
        }
        else
        {
            AddUri(resultIntent.Data);
        }

        return uris;

        void AddUri(AndroidUri? uri)
        {
            var key = uri?.ToString();
            if (uri is not null && !string.IsNullOrWhiteSpace(key) && seen.Add(key))
            {
                uris.Add(uri);
            }
        }
    }

    private static SelectedUri Describe(
        Context context,
        ContentResolver resolver,
        AndroidUri uri)
    {
        string? displayName = null;
        long fileSize = 0;
        try
        {
            var displayNameColumn = IOpenableColumns.DisplayName;
            var sizeColumn = IOpenableColumns.Size;
            if (string.IsNullOrWhiteSpace(displayNameColumn)
                || string.IsNullOrWhiteSpace(sizeColumn))
            {
                throw new InvalidOperationException("Android openable columns are unavailable.");
            }

            using var cursor = resolver.Query(
                uri,
                [displayNameColumn, sizeColumn],
                null,
                null,
                null);
            if (cursor?.MoveToFirst() == true)
            {
                var nameIndex = cursor.GetColumnIndex(displayNameColumn);
                var sizeIndex = cursor.GetColumnIndex(sizeColumn);
                if (nameIndex >= 0 && !cursor.IsNull(nameIndex))
                {
                    displayName = cursor.GetString(nameIndex);
                }

                if (sizeIndex >= 0 && !cursor.IsNull(sizeIndex))
                {
                    fileSize = cursor.GetLong(sizeIndex);
                }
            }
        }
        catch
        {
            // The stream staging step remains authoritative for readable bytes and size.
        }

        var mediaDataPath = QueryMediaDataPath(resolver, uri);
        if (mediaDataPath is null && OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var mediaStoreUri = ResolveMediaStoreUri(context, uri);
            if (mediaStoreUri is not null)
            {
                mediaDataPath = QueryMediaDataPath(resolver, mediaStoreUri);
            }
        }

        var fileName = AndroidOriginalMediaAccessPolicy.ResolveOriginalFileName(
                           mediaDataPath,
                           displayName,
                           uri.LastPathSegment)
                       ?? $"selected-{Guid.NewGuid():N}";
        var contentType = resolver.GetType(uri);
        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = ResolveContentType(fileName);
        }

        var isPhoto = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                      || IsPhotoExtension(Path.GetExtension(fileName));
        return new SelectedUri(uri, fileName, contentType, fileSize, isPhoto);
    }

    private static string? QueryMediaDataPath(ContentResolver resolver, AndroidUri uri)
    {
        if (!string.Equals(uri.Authority, "media", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var cursor = resolver.Query(uri, [MediaDataColumn], null, null, null);
            if (cursor?.MoveToFirst() != true)
            {
                return null;
            }

            var dataIndex = cursor.GetColumnIndex(MediaDataColumn);
            return dataIndex >= 0 && !cursor.IsNull(dataIndex)
                ? cursor.GetString(dataIndex)
                : null;
        }
        catch
        {
            // Some document/cloud providers do not expose MediaStore's backing path.
            return null;
        }
    }

    [SupportedOSPlatform("android29.0")]
    private static AndroidUri? ResolveMediaStoreUri(Context context, AndroidUri selectedUri)
    {
        if (string.Equals(selectedUri.Authority, "media", StringComparison.OrdinalIgnoreCase))
        {
            return selectedUri;
        }

        try
        {
            return MediaStore.GetMediaUri(context, selectedUri);
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("android29.0")]
    private static AndroidUri? RequireOriginalUri(AndroidUri mediaStoreUri) =>
        MediaStore.SetRequireOriginal(mediaStoreUri);

    private static MobileSelectedMediaFile Unavailable(
        SelectedUri selection,
        string code,
        string message) => new(
        selection.FileName,
        selection.ContentType,
        selection.FileSize,
        _ => Task.FromException<Stream>(new MobileUploadException(code, message)));

    private static bool IsPhotoExtension(string extension) =>
        extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp"
            or ".heic" or ".heif";

    private static string ResolveContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
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

    private sealed record SelectedUri(
        AndroidUri Uri,
        string FileName,
        string ContentType,
        long FileSize,
        bool IsPhoto);
}
