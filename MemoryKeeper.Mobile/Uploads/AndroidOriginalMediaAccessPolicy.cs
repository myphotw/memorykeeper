namespace MemoryKeeper.Mobile.Uploads;

public enum AndroidOriginalMediaReadMode
{
    Direct,
    RequireMediaStoreOriginal,
    PermissionRequired,
    UnsupportedProvider,
}

public static class AndroidOriginalMediaAccessPolicy
{
    public const int Android10ApiLevel = 29;

    public static AndroidOriginalMediaReadMode Decide(
        int apiLevel,
        bool isPhoto,
        bool hasMediaLocationPermission,
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

        return hasMediaStoreUri
            ? AndroidOriginalMediaReadMode.RequireMediaStoreOriginal
            : AndroidOriginalMediaReadMode.UnsupportedProvider;
    }
}
