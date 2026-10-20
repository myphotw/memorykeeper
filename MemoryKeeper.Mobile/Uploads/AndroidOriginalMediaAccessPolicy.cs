namespace MemoryKeeper.Mobile.Uploads;

public enum AndroidOriginalMediaReadMode
{
    Direct,
    LocationAwarePhotoPicker,
    RequireMediaStoreOriginal,
    PermissionRequired,
    UnsupportedProvider,
}

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

    public static string? ResolveOriginalFileName(
        string? mediaDataPath,
        string? displayName,
        string? uriLastPathSegment)
    {
        var mediaFileName = GetLeafName(mediaDataPath);
        if (!string.IsNullOrWhiteSpace(mediaFileName))
        {
            return mediaFileName;
        }

        var openableFileName = GetLeafName(displayName);
        if (string.IsNullOrWhiteSpace(openableFileName)
            || IsProviderGeneratedIdName(openableFileName, uriLastPathSegment))
        {
            return null;
        }

        return openableFileName;
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

    private static bool IsProviderGeneratedIdName(
        string fileName,
        string? uriLastPathSegment)
    {
        var uriId = GetLeafName(uriLastPathSegment);
        if (string.IsNullOrEmpty(uriId) || !uriId.All(char.IsDigit))
        {
            return false;
        }

        var extensionIndex = fileName.LastIndexOf('.');
        var fileNameWithoutExtension = extensionIndex > 0
            ? fileName[..extensionIndex]
            : fileName;
        return string.Equals(fileNameWithoutExtension, uriId, StringComparison.Ordinal);
    }
}
