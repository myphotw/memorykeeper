namespace MemoryKeeper.Mobile.Uploads;

public enum AndroidOriginalMediaReadMode
{
    Direct,
    LocationAwarePhotoPicker,
    RequireMediaStoreOriginal,
    PermissionRequired,
    UnsupportedProvider,
}

public enum AndroidMediaUriKind
{
    Other,
    Document,
    PhotoPicker,
    PhotoPickerGetContent,
    MediaStoreImage,
    MediaStoreVideo,
    MediaStoreFile,
}

internal readonly record struct AndroidProviderMediaIdAnalysis(
    string? Candidate,
    string Status,
    bool HasExtension);

public sealed record AndroidOriginalFileNameResolution(
    string? FileName,
    string Source,
    string Reason,
    string SelectedDisplayDecision,
    string SelectedDataDecision,
    string ConvertedDisplayDecision,
    string ConvertedDataDecision);

public static class AndroidOriginalMediaAccessPolicy
{
    public const int Android10ApiLevel = 29;
    public const int Android13ApiLevel = 33;
    public const int PhotoPickerLocationMetadataExtensionVersion = 23;

    public static bool SupportsLocationAwarePhotoPicker(
        int apiLevel,
        int android13ExtensionVersion) =>
        apiLevel >= Android13ApiLevel
        && android13ExtensionVersion >= PhotoPickerLocationMetadataExtensionVersion;

    public static AndroidMediaUriKind ClassifyUri(
        string? authority,
        string? path,
        bool isDocumentUri)
    {
        if (isDocumentUri)
        {
            return AndroidMediaUriKind.Document;
        }

        if (!string.Equals(authority, "media", StringComparison.OrdinalIgnoreCase))
        {
            return AndroidMediaUriKind.Other;
        }

        var segments = (path ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => string.Equals(
                segment,
                "picker_get_content",
                StringComparison.OrdinalIgnoreCase)))
        {
            return AndroidMediaUriKind.PhotoPickerGetContent;
        }

        if (segments.Any(segment => string.Equals(
                segment,
                "picker",
                StringComparison.OrdinalIgnoreCase)))
        {
            return AndroidMediaUriKind.PhotoPicker;
        }

        if (segments.Length < 3
            || AnalyzeProviderMediaId(segments[^1]).Candidate is null)
        {
            return AndroidMediaUriKind.Other;
        }

        if (string.Equals(segments[^2], "file", StringComparison.OrdinalIgnoreCase))
        {
            return AndroidMediaUriKind.MediaStoreFile;
        }

        if (segments.Length >= 4
            && string.Equals(segments[^2], "media", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(segments[^3], "images", StringComparison.OrdinalIgnoreCase))
            {
                return AndroidMediaUriKind.MediaStoreImage;
            }

            if (string.Equals(segments[^3], "video", StringComparison.OrdinalIgnoreCase))
            {
                return AndroidMediaUriKind.MediaStoreVideo;
            }
        }

        return AndroidMediaUriKind.Other;
    }

    public static AndroidOriginalFileNameResolution ResolveOriginalFileName(
        AndroidMediaUriKind selectedUriKind,
        string? selectedDisplayName,
        string? selectedDataPath,
        string? selectedUriLastPathSegment,
        AndroidMediaUriKind convertedUriKind,
        string? convertedDisplayName,
        string? convertedDataPath,
        string? convertedUriLastPathSegment)
    {
        var selectedDisplay = EvaluateDisplayCandidate(
            selectedDisplayName,
            selectedUriLastPathSegment,
            selectedUriKind);
        var convertedDisplay = EvaluateDisplayCandidate(
            convertedDisplayName,
            convertedUriLastPathSegment,
            convertedUriKind);
        var selectedDataDecision = EvaluateDataCandidate(
            selectedDataPath,
            selectedUriLastPathSegment,
            selectedUriKind,
            selectedDisplay.FileName);
        var convertedDataDecision = EvaluateDataCandidate(
            convertedDataPath,
            convertedUriLastPathSegment,
            convertedUriKind,
            convertedDisplay.FileName);

        if (IsMediaStoreItem(convertedUriKind) && convertedDisplay.FileName is not null)
        {
            return new AndroidOriginalFileNameResolution(
                convertedDisplay.FileName,
                "converted_display",
                "official_mediastore_display",
                selectedDisplay.Decision,
                selectedDataDecision,
                convertedDisplay.Decision,
                convertedDataDecision);
        }

        if (selectedDisplay.FileName is not null)
        {
            return new AndroidOriginalFileNameResolution(
                selectedDisplay.FileName,
                "selected_display",
                "openable_display",
                selectedDisplay.Decision,
                selectedDataDecision,
                convertedDisplay.Decision,
                convertedDataDecision);
        }

        return new AndroidOriginalFileNameResolution(
            null,
            "fallback",
            "no_trusted_display_name",
            selectedDisplay.Decision,
            selectedDataDecision,
            convertedDisplay.Decision,
            convertedDataDecision);
    }

    public static AndroidOriginalMediaReadMode Decide(
        int apiLevel,
        bool isPhoto,
        bool hasMediaLocationPermission,
        bool usedLocationAwarePhotoPicker,
        bool hasMediaStoreUri)
    {
        if (!isPhoto || apiLevel < Android10ApiLevel)
        {
            return AndroidOriginalMediaReadMode.Direct;
        }

        if (!hasMediaLocationPermission)
        {
            return AndroidOriginalMediaReadMode.PermissionRequired;
        }

        if (usedLocationAwarePhotoPicker)
        {
            return AndroidOriginalMediaReadMode.LocationAwarePhotoPicker;
        }

        return hasMediaStoreUri
            ? AndroidOriginalMediaReadMode.RequireMediaStoreOriginal
            : AndroidOriginalMediaReadMode.UnsupportedProvider;
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

    private static (string? FileName, string Decision) EvaluateDisplayCandidate(
        string? value,
        string? uriLastPathSegment,
        AndroidMediaUriKind uriKind)
    {
        var fileName = GetLeafName(value);
        if (fileName is null)
        {
            return (null, "missing");
        }

        return IsProviderGeneratedIdName(fileName, uriLastPathSegment, uriKind)
            ? (null, "rejected_uri_id")
            : (fileName, "accepted");
    }

    private static string EvaluateDataCandidate(
        string? value,
        string? uriLastPathSegment,
        AndroidMediaUriKind uriKind,
        string? acceptedDisplayName)
    {
        var fileName = GetLeafName(value);
        if (fileName is null)
        {
            return "missing";
        }

        if (IsProviderGeneratedIdName(fileName, uriLastPathSegment, uriKind))
        {
            return "rejected_uri_id";
        }

        return string.Equals(fileName, acceptedDisplayName, StringComparison.Ordinal)
            ? "corroborates_display"
            : "not_authoritative";
    }

    private static bool IsProviderGeneratedIdName(
        string fileName,
        string? uriLastPathSegment,
        AndroidMediaUriKind uriKind)
    {
        if (uriKind is not (AndroidMediaUriKind.PhotoPicker
            or AndroidMediaUriKind.PhotoPickerGetContent
            or AndroidMediaUriKind.MediaStoreImage
            or AndroidMediaUriKind.MediaStoreVideo
            or AndroidMediaUriKind.MediaStoreFile))
        {
            return false;
        }

        var uriId = AnalyzeProviderMediaId(uriLastPathSegment).Candidate;
        if (uriId is null)
        {
            return false;
        }

        var extensionIndex = fileName.LastIndexOf('.');
        var fileNameWithoutExtension = extensionIndex > 0
            ? fileName[..extensionIndex]
            : fileName;
        return string.Equals(fileNameWithoutExtension, uriId, StringComparison.Ordinal);
    }

    internal static AndroidProviderMediaIdAnalysis AnalyzeProviderMediaId(
        string? uriLastPathSegment)
    {
        var leafName = GetLeafName(uriLastPathSegment);
        if (leafName is null)
        {
            return new AndroidProviderMediaIdAnalysis(null, "missing", false);
        }

        var extensionIndex = leafName.LastIndexOf('.');
        var hasExtension = extensionIndex > 0 && extensionIndex < leafName.Length - 1;
        var candidate = hasExtension ? leafName[..extensionIndex] : leafName;
        return candidate.Length > 0 && candidate.All(char.IsDigit)
            ? new AndroidProviderMediaIdAnalysis(candidate, "numeric", hasExtension)
            : new AndroidProviderMediaIdAnalysis(null, "not_numeric", hasExtension);
    }

    internal static string DisplayNameMatchesProviderMediaId(
        string? displayName,
        string? uriLastPathSegment)
    {
        var fileName = GetLeafName(displayName);
        var uriId = AnalyzeProviderMediaId(uriLastPathSegment).Candidate;
        if (fileName is null || uriId is null)
        {
            return "unknown";
        }

        var extensionIndex = fileName.LastIndexOf('.');
        var fileNameWithoutExtension = extensionIndex > 0
            ? fileName[..extensionIndex]
            : fileName;
        return string.Equals(fileNameWithoutExtension, uriId, StringComparison.Ordinal)
            ? "true"
            : "false";
    }

    private static bool IsMediaStoreItem(AndroidMediaUriKind kind) => kind is
        AndroidMediaUriKind.MediaStoreImage
        or AndroidMediaUriKind.MediaStoreVideo
        or AndroidMediaUriKind.MediaStoreFile;
}
