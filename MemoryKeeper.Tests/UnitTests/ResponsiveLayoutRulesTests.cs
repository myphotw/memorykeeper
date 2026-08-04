using MemoryKeeper.Application.Layout;

namespace MemoryKeeper.Tests.UnitTests;

public class ResponsiveLayoutRulesTests
{
    [Theory]
    [InlineData(0, LayoutBreakpoint.Small)]
    [InlineData(800, LayoutBreakpoint.Small)]
    [InlineData(1199.9, LayoutBreakpoint.Small)]
    [InlineData(1200, LayoutBreakpoint.Medium)]
    [InlineData(1500, LayoutBreakpoint.Medium)]
    [InlineData(1699.9, LayoutBreakpoint.Medium)]
    [InlineData(1700, LayoutBreakpoint.Large)]
    [InlineData(2400, LayoutBreakpoint.Large)]
    public void FromWidth_MapsBreakpoints(double width, LayoutBreakpoint expected)
    {
        Assert.Equal(expected, ResponsiveLayoutRules.FromWidth(width));
    }

    [Fact]
    public void FilmStripVisibleRadius_ShrinksOnSmall()
    {
        Assert.True(
            ResponsiveLayoutRules.FilmStripVisibleRadius(LayoutBreakpoint.Small)
            < ResponsiveLayoutRules.FilmStripVisibleRadius(LayoutBreakpoint.Large));
    }

    [Fact]
    public void UseStackedColumns_OnlyOnSmall()
    {
        Assert.True(ResponsiveLayoutRules.UseStackedColumns(LayoutBreakpoint.Small));
        Assert.False(ResponsiveLayoutRules.UseStackedColumns(LayoutBreakpoint.Medium));
        Assert.False(ResponsiveLayoutRules.UseStackedColumns(LayoutBreakpoint.Large));
    }

    [Fact]
    public void ContentMaxWidth_GrowsWithBreakpoint()
    {
        var small = ResponsiveLayoutRules.ContentMaxWidth(LayoutBreakpoint.Small);
        var medium = ResponsiveLayoutRules.ContentMaxWidth(LayoutBreakpoint.Medium);
        var large = ResponsiveLayoutRules.ContentMaxWidth(LayoutBreakpoint.Large);
        Assert.True(small < medium);
        Assert.True(medium < large);
    }
}
