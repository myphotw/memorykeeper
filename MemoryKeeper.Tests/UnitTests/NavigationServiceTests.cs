using MemoryKeeper.Application.Navigation;

namespace MemoryKeeper.Tests.UnitTests;

public class NavigationServiceTests
{
    [Fact]
    public void Navigate_PushesBack_AndClearsForward()
    {
        var nav = new NavigationService();
        nav.Navigate(NavigationEntry.Of("home"));
        nav.Navigate(NavigationEntry.Of("gallery"));
        nav.Navigate(NavigationEntry.Of("visits"));

        Assert.True(nav.CanGoBack);
        Assert.False(nav.CanGoForward);
        Assert.Equal("visits", nav.Current?.Tag);

        Assert.True(nav.TryGoBack(out var back));
        Assert.Equal("gallery", back.Tag);
        Assert.True(nav.CanGoForward);

        nav.Navigate(NavigationEntry.Of("travel"));
        Assert.False(nav.CanGoForward);
        Assert.Equal("travel", nav.Current?.Tag);
    }

    [Fact]
    public void TryGoForward_RestoresForwardStack()
    {
        var nav = new NavigationService();
        nav.Navigate(NavigationEntry.Of("home"));
        nav.Navigate(NavigationEntry.Of("gallery"));
        nav.Navigate(NavigationEntry.Of("photo-viewer"));

        Assert.True(nav.TryGoBack(out _));
        Assert.Equal("gallery", nav.Current?.Tag);
        Assert.True(nav.TryGoForward(out var forward));
        Assert.Equal("photo-viewer", forward.Tag);
        Assert.Equal("photo-viewer", nav.Current?.Tag);
    }

    [Fact]
    public void NavigateRoot_ClearsHistory()
    {
        var nav = new NavigationService();
        nav.Navigate(NavigationEntry.Of("gallery"));
        nav.Navigate(NavigationEntry.Of("visits"));
        nav.NavigateRoot(NavigationEntry.Home);

        Assert.False(nav.CanGoBack);
        Assert.False(nav.CanGoForward);
        Assert.Equal("home", nav.Current?.Tag);
    }

    [Fact]
    public void BackFromVisits_ReturnsCallerNotForcedHome()
    {
        var nav = new NavigationService();
        nav.Navigate(NavigationEntry.Of("home"));
        nav.Navigate(NavigationEntry.Of("gallery"));
        nav.Navigate(NavigationEntry.Of("visits"));

        Assert.True(nav.TryGoBack(out var entry));
        Assert.Equal("gallery", entry.Tag);
    }

    [Fact]
    public void NavigateIfNeeded_SkipsSameEntry()
    {
        var nav = new NavigationService();
        nav.Navigate(NavigationEntry.Of("gallery"));
        nav.Navigate(NavigationEntry.Of("photo-viewer"));

        Assert.False(nav.NavigateIfNeeded(NavigationEntry.Of("photo-viewer")));
        Assert.True(nav.CanGoBack);
        Assert.Equal("photo-viewer", nav.Current?.Tag);

        Assert.True(nav.NavigateIfNeeded(NavigationEntry.Of("photo")));
        Assert.Equal("photo", nav.Current?.Tag);
        Assert.True(nav.TryGoBack(out var back));
        Assert.Equal("photo-viewer", back.Tag);
        Assert.True(nav.TryGoBack(out var gallery));
        Assert.Equal("gallery", gallery.Tag);
    }

    [Fact]
    public void Gallery_Viewer_Detail_Back_Back_ReturnsGallery()
    {
        var nav = new NavigationService();
        nav.Navigate(NavigationEntry.Of("gallery"));
        nav.Navigate(NavigationEntry.Of("photo-viewer"));
        nav.Navigate(NavigationEntry.Of("photo"));

        Assert.True(nav.TryGoBack(out var viewer));
        Assert.Equal("photo-viewer", viewer.Tag);
        Assert.True(nav.TryGoBack(out var gallery));
        Assert.Equal("gallery", gallery.Tag);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void DetailBack_MustNotPushNewViewer()
    {
        // Simulates the bug: Detail close used Navigate(viewer) instead of GoBack.
        var nav = new NavigationService();
        nav.Navigate(NavigationEntry.Of("gallery"));
        nav.Navigate(NavigationEntry.Of("photo-viewer"));
        nav.Navigate(NavigationEntry.Of("photo"));

        // Wrong: push viewer again
        nav.Navigate(NavigationEntry.Of("photo-viewer"));
        Assert.Equal(new[] { "gallery", "photo-viewer", "photo" }, nav.GetBackStackTags().ToArray());

        // Correct path uses GoBack only — stack stays gallery > viewer after detail.
        var correct = new NavigationService();
        correct.Navigate(NavigationEntry.Of("gallery"));
        correct.Navigate(NavigationEntry.Of("photo-viewer"));
        correct.Navigate(NavigationEntry.Of("photo"));
        Assert.True(correct.TryGoBack(out _));
        Assert.Equal(new[] { "gallery" }, correct.GetBackStackTags().ToArray());
        Assert.Equal("photo-viewer", correct.Current?.Tag);
    }

    [Fact]
    public void PageState_SavePeekTake()
    {
        var nav = new NavigationService();
        nav.SavePageState("gallery", new NavigationPageState
        {
            SearchText = "osaka",
            ScrollPosition = 120,
            ExpandedNodeKeys = ["year:2026", "place:japan"]
        });

        var peek = nav.PeekPageState("gallery");
        Assert.NotNull(peek);
        Assert.Equal("osaka", peek!.SearchText);

        var taken = nav.TakePageState("gallery");
        Assert.NotNull(taken);
        Assert.Null(nav.PeekPageState("gallery"));
    }
}
