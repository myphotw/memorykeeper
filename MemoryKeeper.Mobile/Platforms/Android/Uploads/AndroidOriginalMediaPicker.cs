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
        var displayNameStatus = "not_queried";
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
                displayNameStatus = "missing";
                var nameIndex = cursor.GetColumnIndex(displayNameColumn);
                var sizeIndex = cursor.GetColumnIndex(sizeColumn);
                if (nameIndex >= 0 && !cursor.IsNull(nameIndex))
                {
                    displayName = cursor.GetString(nameIndex);
                    displayNameStatus = string.IsNullOrWhiteSpace(displayName)
                        ? "empty"
                        : "value";
                }

                if (sizeIndex >= 0 && !cursor.IsNull(sizeIndex))
                {
                    fileSize = cursor.GetLong(sizeIndex);
                }
            }
            else
            {
                displayNameStatus = "no_row";
            }
        }
        catch
        {
            displayNameStatus = "query_failed";
            // The stream staging step remains authoritative for readable bytes and size.
        }

        var selectedDataPath = QueryMediaDataPath(resolver, uri, out var selectedDataStatus);
        var mediaDataPath = selectedDataPath;
        var conversionAttempted = false;
        AndroidUri? convertedMediaStoreUri = null;
        string? convertedDisplayName = null;
        var convertedDisplayNameStatus = "not_queried";
        string? convertedDataPath = null;
        var convertedDataStatus = "not_queried";
        if (mediaDataPath is null && OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            conversionAttempted = true;
            convertedMediaStoreUri = ResolveMediaStoreUri(context, uri);
            if (convertedMediaStoreUri is not null)
            {
                convertedDisplayName = QueryDisplayName(
                    resolver,
                    convertedMediaStoreUri,
                    out convertedDisplayNameStatus);
                convertedDataPath = QueryMediaDataPath(
                    resolver,
                    convertedMediaStoreUri,
                    out convertedDataStatus);
                mediaDataPath = convertedDataPath;
            }
        }

        var resolvedFileName = AndroidOriginalMediaAccessPolicy.ResolveOriginalFileName(
            mediaDataPath,
            displayName,
            uri.LastPathSegment);
        var fileNameSource = ResolveFileNameSource(
            resolvedFileName,
            selectedDataPath,
            convertedDataPath);
        var fileName = resolvedFileName ?? $"selected-{Guid.NewGuid():N}";
        LogFileNameDiagnostics(
            context,
            uri,
            displayName,
            displayNameStatus,
            selectedDataPath,
            selectedDataStatus,
            conversionAttempted,
            convertedMediaStoreUri,
            convertedDisplayName,
            convertedDisplayNameStatus,
            convertedDataPath,
            convertedDataStatus,
            fileName,
            fileNameSource);
        var contentType = resolver.GetType(uri);
        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = ResolveContentType(fileName);
        }

        var isPhoto = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                      || IsPhotoExtension(Path.GetExtension(fileName));
        return new SelectedUri(uri, fileName, contentType, fileSize, isPhoto);
    }

    private static string? QueryMediaDataPath(
        ContentResolver resolver,
        AndroidUri uri,
        out string status)
    {
        if (!string.Equals(uri.Authority, "media", StringComparison.OrdinalIgnoreCase))
        {
            status = "not_media";
            return null;
        }

        try
        {
            using var cursor = resolver.Query(uri, [MediaDataColumn], null, null, null);
            if (cursor?.MoveToFirst() != true)
            {
                status = "no_row";
                return null;
            }

            var dataIndex = cursor.GetColumnIndex(MediaDataColumn);
            if (dataIndex < 0 || cursor.IsNull(dataIndex))
            {
                status = "missing";
                return null;
            }

            var value = cursor.GetString(dataIndex);
            status = string.IsNullOrWhiteSpace(value) ? "empty" : "value";
            return value;
        }
        catch
        {
            status = "query_failed";
            // Some document/cloud providers do not expose MediaStore's backing path.
            return null;
        }
    }

    private static string? QueryDisplayName(
        ContentResolver resolver,
        AndroidUri uri,
        out string status)
    {
        try
        {
            var displayNameColumn = IOpenableColumns.DisplayName;
            if (string.IsNullOrWhiteSpace(displayNameColumn))
            {
                status = "column_unavailable";
                return null;
            }

            using var cursor = resolver.Query(uri, [displayNameColumn], null, null, null);
            if (cursor?.MoveToFirst() != true)
            {
                status = "no_row";
                return null;
            }

            var nameIndex = cursor.GetColumnIndex(displayNameColumn);
            if (nameIndex < 0 || cursor.IsNull(nameIndex))
            {
                status = "missing";
                return null;
            }

            var value = cursor.GetString(nameIndex);
            status = string.IsNullOrWhiteSpace(value) ? "empty" : "value";
            return value;
        }
        catch
        {
            status = "query_failed";
            return null;
        }
    }

    private static string ResolveFileNameSource(
        string? resolvedFileName,
        string? selectedDataPath,
        string? convertedDataPath)
    {
        if (resolvedFileName is null)
        {
            return "fallback";
        }

        if (string.Equals(
                GetLeafName(selectedDataPath),
                resolvedFileName,
                StringComparison.Ordinal))
        {
            return "selected_data";
        }

        if (string.Equals(
                GetLeafName(convertedDataPath),
                resolvedFileName,
                StringComparison.Ordinal))
        {
            return "converted_data";
        }

        return "selected_display";
    }

    private static void LogFileNameDiagnostics(
        Context context,
        AndroidUri selectedUri,
        string? selectedDisplayName,
        string selectedDisplayStatus,
        string? selectedDataPath,
        string selectedDataStatus,
        bool conversionAttempted,
        AndroidUri? convertedMediaStoreUri,
        string? convertedDisplayName,
        string convertedDisplayStatus,
        string? convertedDataPath,
        string convertedDataStatus,
        string finalFileName,
        string finalFileNameSource)
    {
        try
        {
            var isDocumentUri = DocumentsContract.IsDocumentUri(context, selectedUri);
            var selectedDataName = GetLeafName(selectedDataPath);
            var convertedDataName = GetLeafName(convertedDataPath);
            global::Android.Util.Log.Info(
                DiagnosticTag,
                $"MK_UPLOAD_FILENAME event=metadata "
                + $"scheme={SafeToken(selectedUri.Scheme)} "
                + $"authority={SafeToken(selectedUri.Authority)} "
                + $"path_type={ClassifyUriPath(selectedUri, isDocumentUri)} "
                + $"document_uri={(isDocumentUri ? "true" : "false")} "
                + $"last_segment_shape={NameShape(selectedUri.LastPathSegment)} "
                + $"selected_display_status={selectedDisplayStatus} "
                + $"selected_display_shape={NameShape(selectedDisplayName)} "
                + $"selected_display_ext={ExtensionToken(selectedDisplayName)} "
                + $"selected_display_length={GetLeafName(selectedDisplayName)?.Length ?? 0} "
                + $"selected_data_status={selectedDataStatus} "
                + $"selected_data_shape={NameShape(selectedDataName)} "
                + $"selected_data_ext={ExtensionToken(selectedDataName)} "
                + $"selected_data_length={selectedDataName?.Length ?? 0} "
                + $"selected_names_match={MatchState(selectedDisplayName, selectedDataName)} "
                + $"conversion_attempted={(conversionAttempted ? "true" : "false")} "
                + $"conversion_succeeded={(convertedMediaStoreUri is not null ? "true" : "false")} "
                + $"converted_path_type={ClassifyConvertedUriPath(convertedMediaStoreUri)} "
                + $"converted_display_status={convertedDisplayStatus} "
                + $"converted_display_shape={NameShape(convertedDisplayName)} "
                + $"converted_display_ext={ExtensionToken(convertedDisplayName)} "
                + $"converted_display_length={GetLeafName(convertedDisplayName)?.Length ?? 0} "
                + $"converted_data_status={convertedDataStatus} "
                + $"converted_data_shape={NameShape(convertedDataName)} "
                + $"converted_data_ext={ExtensionToken(convertedDataName)} "
                + $"converted_data_length={convertedDataName?.Length ?? 0} "
                + $"final_name_shape={NameShape(finalFileName)} "
                + $"final_name_ext={ExtensionToken(finalFileName)} "
                + $"final_name_length={GetLeafName(finalFileName)?.Length ?? 0} "
                + $"final_source={finalFileNameSource}");
        }
        catch
        {
            // Diagnostics must never affect selection or upload.
        }
    }

    private static string ClassifyUriPath(AndroidUri uri, bool isDocumentUri)
    {
        var path = uri.Path ?? string.Empty;
        if (path.Contains("/picker/", StringComparison.OrdinalIgnoreCase))
        {
            return "picker";
        }

        if (isDocumentUri)
        {
            return "document";
        }

        if (string.Equals(uri.Authority, "media", StringComparison.OrdinalIgnoreCase)
            && (path.Contains("/images/media/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/video/media/", StringComparison.OrdinalIgnoreCase)))
        {
            return "external_media";
        }

        return "other";
    }

    private static string ClassifyConvertedUriPath(AndroidUri? uri) => uri is null
        ? "none"
        : ClassifyUriPath(uri, false);

    private static string MatchState(string? first, string? second)
    {
        var firstName = GetLeafName(first);
        var secondName = GetLeafName(second);
        if (firstName is null || secondName is null)
        {
            return "unknown";
        }

        return string.Equals(firstName, secondName, StringComparison.Ordinal)
            ? "true"
            : "false";
    }

    private static string NameShape(string? value)
    {
        var leafName = GetLeafName(value);
        if (leafName is null)
        {
            return "missing";
        }

        var extensionIndex = leafName.LastIndexOf('.');
        var stem = extensionIndex > 0 ? leafName[..extensionIndex] : leafName;
        if (stem.Length > 0 && stem.All(char.IsDigit))
        {
            return "numeric_id";
        }

        return stem.StartsWith("selected-", StringComparison.Ordinal)
            ? "generated"
            : "named";
    }

    private static string ExtensionToken(string? value)
    {
        var leafName = GetLeafName(value);
        if (leafName is null)
        {
            return "none";
        }

        var extensionIndex = leafName.LastIndexOf('.');
        if (extensionIndex < 0 || extensionIndex == leafName.Length - 1)
        {
            return "none";
        }

        var extension = leafName[(extensionIndex + 1)..];
        return extension.Length <= 10 && extension.All(char.IsLetterOrDigit)
            ? extension.ToLowerInvariant()
            : "other";
    }

    private static string? GetLeafName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var separatorIndex = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
        var leafName = separatorIndex >= 0 ? value[(separatorIndex + 1)..] : value;
        return string.IsNullOrWhiteSpace(leafName) ? null : leafName;
    }

    private static string SafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "missing";
        }

        return value.Length <= 100
               && value.All(character => char.IsLetterOrDigit(character)
                                         || character is '.' or '_' or '-')
            ? value.ToLowerInvariant()
            : "other";
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
