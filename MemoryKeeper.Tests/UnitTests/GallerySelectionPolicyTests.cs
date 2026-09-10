using MemoryKeeper.Application;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class GallerySelectionPolicyTests
{
    [Theory]
    [InlineData(0, 0, GallerySelectionPolicy.SelectAllLabel)]
    [InlineData(50, 0, GallerySelectionPolicy.SelectAllLabel)]
    [InlineData(50, 12, GallerySelectionPolicy.SelectAllLabel)]
    [InlineData(50, 49, GallerySelectionPolicy.SelectAllLabel)]
    [InlineData(50, 50, GallerySelectionPolicy.ClearAllLabel)]
    public void GetToggleLabel_UsesLoadedNativeSelectionCounts(
        int loadedCount,
        int selectedCount,
        string expected)
    {
        Assert.Equal(expected, GallerySelectionPolicy.GetToggleLabel(loadedCount, selectedCount));
    }

    [Fact]
    public void GetToggleLabel_ReturnsSelectAllAfterOneItemIsDeselected()
    {
        Assert.Equal(GallerySelectionPolicy.ClearAllLabel, GallerySelectionPolicy.GetToggleLabel(50, 50));
        Assert.Equal(GallerySelectionPolicy.SelectAllLabel, GallerySelectionPolicy.GetToggleLabel(50, 49));
    }

    [Fact]
    public void GetToggleLabel_ReturnsSelectAllAfterLoadMoreWithoutAutoSelectingNewItems()
    {
        Assert.Equal(GallerySelectionPolicy.ClearAllLabel, GallerySelectionPolicy.GetToggleLabel(50, 50));
        Assert.Equal(GallerySelectionPolicy.SelectAllLabel, GallerySelectionPolicy.GetToggleLabel(100, 50));
    }
}
