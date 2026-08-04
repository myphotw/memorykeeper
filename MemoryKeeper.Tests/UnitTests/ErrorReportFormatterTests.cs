using MemoryKeeper.Application.Diagnostics;

namespace MemoryKeeper.Tests.UnitTests;

public class ErrorReportFormatterTests
{
    [Fact]
    public void BuildDisplayText_IncludesExceptionInnerAndStackTrace()
    {
        Exception caught;
        try
        {
            throw new InvalidOperationException(
                "outer",
                new ArgumentException("inner"));
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        var text = ErrorReportFormatter.BuildDisplayText(caught, stage: "Gallery.Load");

        Assert.Contains("Stage: Gallery.Load", text);
        Assert.Contains("Exception:", text);
        Assert.Contains("Type: System.InvalidOperationException", text);
        Assert.Contains("Message: outer", text);
        Assert.Contains("Inner Exception:", text);
        Assert.Contains("Message: inner", text);
        Assert.Contains("StackTrace:", text);
    }

    [Fact]
    public void BuildClipboardText_IncludesTimeVersionExceptionInnerStackTrace()
    {
        var time = new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.FromHours(9));
        var exception = new InvalidOperationException("boom", new Exception("root"));

        var text = ErrorReportFormatter.BuildClipboardText(
            exception,
            stage: "Startup",
            time,
            version: "1.2.3",
            ErrorReportSource.Startup);

        Assert.Contains("Time: 2026-07-28 09:00:00.000 +09:00", text);
        Assert.Contains("Version: 1.2.3", text);
        Assert.Contains("Source: Startup", text);
        Assert.Contains("Exception:", text);
        Assert.Contains("Message: boom", text);
        Assert.Contains("Inner Exception:", text);
        Assert.Contains("Message: root", text);
        Assert.Contains("StackTrace:", text);
    }

    [Theory]
    [InlineData(ErrorReportSource.Gallery, ErrorLogTarget.Gallery)]
    [InlineData(ErrorReportSource.Startup, ErrorLogTarget.Startup)]
    [InlineData(ErrorReportSource.Import, ErrorLogTarget.Startup)]
    [InlineData(ErrorReportSource.Unhandled, ErrorLogTarget.Startup)]
    public void GetLogTarget_MapsGallerySeparately(ErrorReportSource source, ErrorLogTarget expected)
    {
        Assert.Equal(expected, ErrorReportFormatter.GetLogTarget(source));
    }
}
