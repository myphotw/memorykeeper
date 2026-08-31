using MemoryKeeper.Application.Navigation;

namespace MemoryKeeper.Tests.UnitTests;

public class NavigationServiceTests
{
    [Fact]
    public void NavigationEntry_PreservesSemanticMetadata()
    {
        var entry = new NavigationEntry(
            "travel-detail",
            SettingsSection: null,
            Kind: NavigationKind.DrillDown,
            RootTag: "travel",
            ContextKey: "recent",
            DisplayLabel: "최근 다녀온 장소");

        Assert.Equal(NavigationKind.DrillDown, entry.Kind);
        Assert.Equal("travel", entry.RootTag);
        Assert.Equal("recent", entry.ContextKey);
        Assert.Equal("최근 다녀온 장소", entry.DisplayLabel);
    }

    [Fact]
    public void NavigationIdentity_IncludesContextAndRoot_ButNotDisplayLabel()
    {
        var recent = NavigationEntry.DrillDown("travel-detail", "travel", "최근 여행", "recent");
        var renamedRecent = NavigationEntry.DrillDown("travel-detail", "travel", "최근 다녀온 장소", "recent");
        var farthest = NavigationEntry.DrillDown("travel-detail", "travel", "가장 멀리 여행한 장소", "farthest");
        var directVisits = NavigationEntry.TopLevel("visits", "방문지도");
        var contextualVisits = NavigationEntry.DrillDown("visits", "travel", "방문지도");

        Assert.True(recent.HasSameIdentity(renamedRecent));
        Assert.False(recent.HasSameIdentity(farthest));
        Assert.False(directVisits.HasSameIdentity(contextualVisits));
    }

    [Fact]
    public void BackEntry_PeeksWithoutChangingHistory()
    {
        var nav = new NavigationService();
        var travel = NavigationEntry.TopLevel("travel", "여행기록");
        var recent = NavigationEntry.DrillDown("travel-detail", "travel", "최근 다녀온 장소", "recent");
        nav.NavigateRoot(travel);

        Assert.False(nav.CanGoBack);
        Assert.Null(nav.BackEntry);

        nav.Navigate(recent);
        var before = nav.GetBackStackTags().ToArray();
        var peeked = nav.BackEntry;

        Assert.NotNull(peeked);
        Assert.True(peeked.Value.HasSameIdentity(travel));
        Assert.Equal("여행기록", peeked.Value.DisplayLabel);
        Assert.Equal(before, nav.GetBackStackTags().ToArray());
        Assert.True(nav.CanGoBack);
        Assert.True(nav.Current is { } current && current.HasSameIdentity(recent));
    }

    [Fact]
    public void BackEntry_ProvidesLabelsForTravelVisitFlows()
    {
        var nav = new NavigationService();
        var travel = NavigationEntry.TopLevel("travel", "여행기록");
        var recent = NavigationEntry.DrillDown("travel-detail", "travel", "최근 다녀온 장소", "recent");
        var visits = NavigationEntry.DrillDown("visits", "travel", "방문지도");

        nav.NavigateRoot(travel);
        nav.Navigate(recent);
        Assert.Equal("여행기록", nav.BackEntry?.DisplayLabel);

        nav.Navigate(visits);
        Assert.Equal("최근 다녀온 장소", nav.BackEntry?.DisplayLabel);

        nav.NavigateRoot(travel);
        nav.Navigate(visits);
        Assert.Equal("여행기록", nav.BackEntry?.DisplayLabel);
    }

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
    public void TopLevelNavigation_ClearsBackAndForward_AndPreservesRootMeaning()
    {
        var nav = new NavigationService();
        nav.NavigateRoot(NavigationEntry.TopLevel("travel", "여행기록"));
        nav.Navigate(NavigationEntry.DrillDown("visits", "travel", "방문지도"));
        Assert.True(nav.TryGoBack(out _));
        Assert.True(nav.CanGoForward);

        nav.NavigateRoot(NavigationEntry.TopLevel("gallery", "사진첩"));

        Assert.False(nav.CanGoBack);
        Assert.False(nav.CanGoForward);
        Assert.Equal(NavigationKind.TopLevel, nav.Current?.Kind);
        Assert.Equal("gallery", nav.Current?.RootTag);
    }

    [Fact]
    public void DrillDownAndViewer_PreserveSourceRoot_AndParticipateInBackStack()
    {
        var nav = new NavigationService();
        var gallery = NavigationEntry.TopLevel("gallery", "사진첩");
        var viewer = NavigationEntry.Viewer("photo-viewer", "gallery", "사진");

        nav.NavigateRoot(gallery);
        nav.Navigate(viewer);

        Assert.Equal(NavigationKind.Viewer, nav.Current?.Kind);
        Assert.Equal("gallery", nav.Current?.RootTag);
        Assert.True(nav.TryGoBack(out var restored));
        Assert.True(restored.HasSameIdentity(gallery));
        Assert.False(nav.CanGoBack);
        Assert.True(nav.CanGoForward);
    }

    [Fact]
    public void SameRoute_DifferentContext_IsNavigated_ButSameContextIsSkipped()
    {
        var nav = new NavigationService();
        var recent = NavigationEntry.DrillDown("travel-detail", "travel", "최근 여행", "recent");
        var farthest = NavigationEntry.DrillDown("travel-detail", "travel", "가장 멀리 여행한 장소", "farthest");

        nav.NavigateRoot(NavigationEntry.TopLevel("travel", "여행기록"));
        Assert.True(nav.NavigateIfNeeded(recent));
        Assert.False(nav.NavigateIfNeeded(recent with { DisplayLabel = "최근 다녀온 장소" }));
        Assert.True(nav.NavigateIfNeeded(farthest));
        Assert.Equal(new[] { "travel", "travel-detail" }, nav.GetBackStackTags().ToArray());
    }

    [Fact]
    public void TravelRecent_Visits_BackTwice_RestoresExactContext()
    {
        var nav = new NavigationService();
        var travel = NavigationEntry.TopLevel("travel", "여행기록");
        var recent = NavigationEntry.DrillDown("travel-detail", "travel", "최근 다녀온 장소", "recent");
        var visits = NavigationEntry.DrillDown("visits", "travel", "방문지도");

        nav.NavigateRoot(travel);
        nav.Navigate(recent);
        nav.Navigate(visits);

        Assert.True(nav.TryGoBack(out var restoredRecent));
        Assert.True(restoredRecent.HasSameIdentity(recent));
        Assert.Equal("recent", restoredRecent.ContextKey);
        Assert.True(nav.TryGoBack(out var restoredTravel));
        Assert.True(restoredTravel.HasSameIdentity(travel));
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void TravelMemory_Visits_ThenTopLevelGallery_DropsContextualHistory()
    {
        var nav = new NavigationService();
        nav.NavigateRoot(NavigationEntry.TopLevel("travel", "여행기록"));
        nav.Navigate(NavigationEntry.DrillDown("visits", "travel", "방문지도"));

        nav.NavigateRoot(NavigationEntry.TopLevel("gallery", "사진첩"));

        Assert.Equal("gallery", nav.Current?.Tag);
        Assert.Equal(NavigationKind.TopLevel, nav.Current?.Kind);
        Assert.False(nav.CanGoBack);
        Assert.False(nav.CanGoForward);
    }

    [Fact]
    public void BackForward_RestoreDoesNotPushDuplicateHistory()
    {
        var nav = new NavigationService();
        nav.NavigateRoot(NavigationEntry.TopLevel("travel", "여행기록"));
        nav.Navigate(NavigationEntry.DrillDown("travel-detail", "travel", "최근 다녀온 장소", "recent"));
        nav.Navigate(NavigationEntry.DrillDown("visits", "travel", "방문지도"));

        Assert.True(nav.TryGoBack(out var detail));
        nav.ReplaceCurrent(detail);
        Assert.Equal(new[] { "travel" }, nav.GetBackStackTags().ToArray());
        Assert.True(nav.TryGoForward(out var visits));
        nav.ReplaceCurrent(visits);
        Assert.Equal(new[] { "travel", "travel-detail" }, nav.GetBackStackTags().ToArray());
    }

    [Fact]
    public void MaxDepth_RemainsThirtyTwo()
    {
        var nav = new NavigationService();
        nav.NavigateRoot(NavigationEntry.TopLevel("home", "홈"));
        for (var i = 0; i < 40; i++)
        {
            nav.Navigate(NavigationEntry.DrillDown("route", "home", "상세", i.ToString()));
        }

        Assert.Equal(32, nav.GetBackStackTags().Count);
    }

    [Fact]
    public void SettingsSection_RemainsPartOfNavigationIdentity()
    {
        var photo = NavigationEntry.DrillDown("settings", "home", "설정", settingsSection: "photo-management");
        var tags = NavigationEntry.DrillDown("settings", "home", "설정", settingsSection: "tags");

        Assert.False(photo.HasSameIdentity(tags));
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
