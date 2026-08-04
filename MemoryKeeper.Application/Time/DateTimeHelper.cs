namespace MemoryKeeper.Application.Time;

/// <summary>
/// MemoryKeeper DateTime standard helpers.
/// DB/Query: UTC <see cref="DateTime"/>. UI/API: <see cref="DateTimeOffset"/> (local display).
/// </summary>
public static class DateTimeHelper
{
    public static DateTime UtcNow => DateTime.UtcNow;

    public static DateTime ToUtc(DateTimeOffset value) => value.UtcDateTime;

    public static DateTime? ToUtc(DateTimeOffset? value) => value?.UtcDateTime;

    public static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value.ToUniversalTime(), DateTimeKind.Utc);

    public static DateTimeOffset ToUtcOffset(DateTime utc)
        => new(DateTime.SpecifyKind(AsUtc(utc), DateTimeKind.Utc));

    public static DateTimeOffset? ToUtcOffset(DateTime? utc)
        => utc is null ? null : ToUtcOffset(utc.Value);

    public static DateTimeOffset ToLocal(DateTime utc) => ToUtcOffset(utc).ToLocalTime();

    public static DateTimeOffset? ToLocal(DateTime? utc)
        => utc is null ? null : ToLocal(utc.Value);

    public static string FormatLocal(DateTime? utc, string format = "yyyy-MM-dd HH:mm")
        => utc is null ? "-" : ToLocal(utc.Value).ToString(format);

    public static string FormatLocal(DateTimeOffset? utcOrOffset, string format = "yyyy-MM-dd HH:mm")
        => utcOrOffset is null ? "-" : utcOrOffset.Value.ToLocalTime().ToString(format);

    public static string FormatLocalDate(DateTimeOffset? utcOrOffset, string format = "yyyy-MM-dd")
        => FormatLocal(utcOrOffset, format);

    public static DateTime YearStartUtc(int year)
        => new(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static DateTime DayStartUtc(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}
