using MemoryKeeper.App.Services;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class EffectiveCaptureDateFormatterTests
{
    [Fact]
    public void Format_YearPrecisionDisplaysOnlyConfirmedYear()
    {
        var text = EffectiveCaptureDateFormatter.Format(
            "YEAR",
            2018,
            effectiveDate: null,
            effectiveDateTime: null,
            "yyyy.MM.dd",
            "yyyy.MM.dd HH:mm",
            "-");

        Assert.Equal("2018년", text);
    }

    [Fact]
    public void Format_DatePrecisionKeepsExistingUserDateDisplay()
    {
        var text = EffectiveCaptureDateFormatter.Format(
            "DATE",
            2025,
            new DateOnly(2025, 4, 3),
            new DateTimeOffset(2025, 4, 3, 0, 0, 0, TimeSpan.Zero),
            "yyyy.MM.dd",
            "yyyy.MM.dd HH:mm",
            "-");

        Assert.Equal("2025.04.03", text);
    }
}
