using MemoryKeeper.Application.Time;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Calculates visit records from media capture dates.
/// Kept separate so a future VisitRecord entity can replace this calculation.
/// </summary>
public sealed class VisitRecordService
{
    public int CalculateVisitRecordCount(IEnumerable<DateTimeOffset> visitDates)
    {
        return visitDates
            .Select(date => date.Date)
            .Distinct()
            .Count();
    }

    public int CalculateVisitRecordCount(
        IEnumerable<(DateTimeOffset? CapturedAt, DateTimeOffset ImportedAt)> mediaTimestamps)
    {
        return CalculateVisitRecordCount(
            mediaTimestamps.Select(item => item.CapturedAt ?? item.ImportedAt));
    }

    public int CalculateVisitRecordCount(
        IEnumerable<(DateTime? CapturedAt, DateTime ImportedAt)> mediaTimestamps)
    {
        return CalculateVisitRecordCount(
            mediaTimestamps.Select(item =>
                DateTimeHelper.ToUtcOffset(item.CapturedAt) ?? DateTimeHelper.ToUtcOffset(item.ImportedAt)));
    }

    public DateTimeOffset ResolveVisitDate(DateTimeOffset? capturedAt, DateTimeOffset importedAt)
    {
        return capturedAt ?? importedAt;
    }

    public DateTimeOffset ResolveVisitDate(DateTime? capturedAt, DateTime importedAt)
    {
        return DateTimeHelper.ToUtcOffset(capturedAt) ?? DateTimeHelper.ToUtcOffset(importedAt);
    }
}
