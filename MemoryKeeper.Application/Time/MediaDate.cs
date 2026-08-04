namespace MemoryKeeper.Application.Time;

/// <summary>
/// Shared capture/import year resolution for gallery and visit map.
/// Uses local calendar year so UI year groups match displayed local dates.
/// </summary>
public static class MediaDate
{
    public static int ResolveYear(DateTime? capturedAt, DateTime importedAt)
    {
        var utc = capturedAt ?? importedAt;
        return DateTimeHelper.ToLocal(utc).Year;
    }

    public static int ResolveYear(DateTimeOffset? capturedAt, DateTimeOffset importedAt)
    {
        var value = capturedAt ?? importedAt;
        return value.ToLocalTime().Year;
    }

    public static int ResolveYear(DateTimeOffset? capturedAt)
    {
        if (capturedAt is DateTimeOffset value)
        {
            return value.ToLocalTime().Year;
        }

        return 0;
    }
}
