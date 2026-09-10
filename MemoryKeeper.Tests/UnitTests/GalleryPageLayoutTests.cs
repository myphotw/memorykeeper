namespace MemoryKeeper.Tests.UnitTests;

public sealed class GalleryPageLayoutTests
{
    [Fact]
    public void Gallery_RemovesRepresentativeHero_AndPlacesGridDirectlyBelowHeader()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml"));
        var codeBehind = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs"));

        Assert.DoesNotContain("HeroThumbHost", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HeroThumbImage", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HeroThumbHost", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<!-- Photo grid -->", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Grid.Row=\"1\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"DetailCard\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenMediaViewerButton", xaml, StringComparison.Ordinal);
        Assert.Contains("DoubleTapped=\"PhotoCard_OnDoubleTapped\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.OpenPhotoViewerCommand.Execute(item)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Text=\"상세\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"PhotoDetail_OnClick\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryThumbnailCards_PreserveWholeImageContext()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml"));
        var cardStart = xaml.IndexOf("Style=\"{StaticResource GalleryPhotoCardStyle}\"", StringComparison.Ordinal);
        var cardEnd = xaml.IndexOf("<!-- Soft primary wash", cardStart, StringComparison.Ordinal);

        Assert.True(cardStart >= 0 && cardEnd > cardStart);
        var card = xaml[cardStart..cardEnd];
        Assert.Contains("Source=\"{Binding ThumbnailImage, Mode=OneWay}\"", card, StringComparison.Ordinal);
        Assert.Contains("Stretch=\"Uniform\"", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Stretch=\"UniformToFill\"", card, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryShortcuts_UseAuthoritativeCountsAndPlaceCleanupRoute()
    {
        var gallery = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs"));
        var page = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs"));
        var pending = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "PendingMemoryViewModel.cs"));
        var pendingView = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "PendingMemoryView.xaml"));

        Assert.Contains("Count = summary.FavoriteCount", gallery, StringComparison.Ordinal);
        Assert.Contains("Count = summary.RecentCount", gallery, StringComparison.Ordinal);
        Assert.Contains("Title = \"장소 정리 필요\"", gallery, StringComparison.Ordinal);
        Assert.Contains("Count = summary.PlaceCleanupCount", gallery, StringComparison.Ordinal);
        Assert.Contains("GalleryTreeNodeKind.Pending", gallery, StringComparison.Ordinal);
        Assert.Contains("OpenPendingRequested?.Invoke", page, StringComparison.Ordinal);
        Assert.Contains("GetPlaceCleanupMemoriesAsync", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPendingMemoriesAsync", pending, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding LoadMoreCleanupCommand}\"", pendingView, StringComparison.Ordinal);
        Assert.Contains("CleanupProgressText", pendingView, StringComparison.Ordinal);
        Assert.Contains("LibraryConstants.UnclassifiedTitle", gallery, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceCleanup_SelectionAndRefreshGuardsArePreserved()
    {
        var model = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Models", "PendingMemoryGroupItem.cs"));
        var view = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "PendingMemoryView.xaml"));
        var codeBehind = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "PendingMemoryView.xaml.cs"));
        var pending = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "PendingMemoryViewModel.cs"));
        var cleanupDiagnostics = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Diagnostics", "PlaceCleanupDiagnostics.cs"));
        var gallery = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs"));

        Assert.Contains("IsIncluded = false", model, StringComparison.Ordinal);
        Assert.Contains("item.IsIncluded = true", pending, StringComparison.Ordinal);
        Assert.Contains("if (item is null || IsSelectionMode)", pending, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ActivateMediaCommand.Execute(item)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ViewModel.OpenPhotoDetailCommand.Execute(item)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Tapped=\"IncludeCheckBox_OnTapped\"", view, StringComparison.Ordinal);
        Assert.Contains("ApplyOverview(overview, preserveSelection: false)", pending, StringComparison.Ordinal);
        Assert.Contains("ApplyOverview(overview, preserveSelection: true)", pending, StringComparison.Ordinal);
        Assert.Contains("_loadedMediaItems = overview.Items", pending, StringComparison.Ordinal);
        Assert.Contains("ActiveMediaItems = new ObservableCollection<PendingMemoryMediaItem>(_loadedMediaItems)", pending, StringComparison.Ordinal);
        Assert.Contains("media.WithEffectiveGeography(registeredPlace)", model, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SelectAllCleanupCommand}\"", view, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding GeographyText}\"", view, StringComparison.Ordinal);
        Assert.Contains("PLACE_CLEANUP_ASSIGN_DIAG", pending, StringComparison.Ordinal);
        Assert.Contains("post_reload_cleanup_selected_count", pending, StringComparison.Ordinal);
        Assert.Contains("post_reload_with_place_id_count", pending, StringComparison.Ordinal);
        Assert.Contains("pre_assign_revision_refresh_failure_count", pending, StringComparison.Ordinal);
        Assert.Contains("conflict_count", pending, StringComparison.Ordinal);
        Assert.Contains("PlaceCleanupDiagnostics.WriteAssignment", pending, StringComparison.Ordinal);
        Assert.Contains("PlaceRadiusExpansionPlanner.Create", pending, StringComparison.Ordinal);
        Assert.Contains("UpdateWithRadiusImpactAsync", pending, StringComparison.Ordinal);
        Assert.Contains("LoadLatestPlaceRevisionsAsync", pending, StringComparison.Ordinal);
        Assert.Contains("ExpectedPlaceRevisions = latestRevisions", pending, StringComparison.Ordinal);
        Assert.Contains("AssignIndividuallyAfterConflictAsync", pending, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!prepared.ReclassificationPerformed)", pending, StringComparison.Ordinal);
        Assert.Contains("VerifyFinalPlaceStateAsync", pending, StringComparison.Ordinal);
        Assert.Contains("PendingPlaceAssignmentOutcomeEvaluator.Evaluate", pending, StringComparison.Ordinal);
        Assert.Contains("place-cleanup-diag.log", cleanupDiagnostics, StringComparison.Ordinal);
        Assert.Contains("StartupDiagnostics.LogDirectory", cleanupDiagnostics, StringComparison.Ordinal);
        Assert.Contains("catch", cleanupDiagnostics, StringComparison.Ordinal);
        Assert.Contains("finally", pending, StringComparison.Ordinal);
        Assert.Contains("await LoadCoreAsync();", pending, StringComparison.Ordinal);
        Assert.Contains("_fastHierarchy = null;", gallery, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceCleanup_RadiusExpansionUsesSharedMapPreviewAndFinalStateOutcome()
    {
        var pending = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "PendingMemoryViewModel.cs"));
        var view = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "PendingMemoryView.xaml.cs"));
        var dialog = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Dialogs", "PlaceRadiusExpansionDialog.cs"));
        var googleMap = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Maps", "Google", "GoogleMapHtmlBuilder.cs"));
        var osmMap = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Maps", "Google", "OpenStreetMapHtmlBuilder.cs"));

        Assert.Contains("RadiusExpansionPreviewHandler", pending, StringComparison.Ordinal);
        Assert.Contains("UpdateWithRadiusImpactAsync", pending, StringComparison.Ordinal);
        Assert.Contains("ReclassificationPerformed", pending, StringComparison.Ordinal);
        Assert.Contains("VerifyFinalPlaceStateAsync", pending, StringComparison.Ordinal);
        Assert.Contains("await LoadCoreAsync();", pending, StringComparison.Ordinal);
        Assert.Contains("PlaceDialogStatus", view, StringComparison.Ordinal);
        Assert.DoesNotContain("위치정보가 등록되었습니다. 사진에 좌표가 반영되었고 미분류에서 제외됩니다.", view, StringComparison.Ordinal);

        Assert.Contains("현재 범위", dialog, StringComparison.Ordinal);
        Assert.Contains("변경 예정", dialog, StringComparison.Ordinal);
        Assert.Contains("현재 범위 밖 사진", dialog, StringComparison.Ordinal);
        Assert.Contains("MapMarkerVisualState.Selected", dialog, StringComparison.Ordinal);
        Assert.Contains("범위 늘리고 등록", dialog, StringComparison.Ordinal);
        Assert.Contains("setRadiusPreview", googleMap, StringComparison.Ordinal);
        Assert.Contains("currentPreviewCircle", googleMap, StringComparison.Ordinal);
        Assert.Contains("proposedPreviewCircle", googleMap, StringComparison.Ordinal);
        Assert.Contains("setRadiusPreview", osmMap, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceCleanup_ManualAssignmentIsTheLastPlaceMutationAfterAutomaticReclassification()
    {
        var pending = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "PendingMemoryViewModel.cs"));

        var confirmStart = pending.IndexOf("public async Task<bool> ConfirmPlaceRegistrationAsync()", StringComparison.Ordinal);
        var confirmEnd = pending.IndexOf("private IReadOnlyList<PlaceRadiusPhotoSelection> BuildRadiusSelections", confirmStart, StringComparison.Ordinal);
        Assert.True(confirmStart >= 0);
        Assert.True(confirmEnd > confirmStart);
        var confirmFlow = pending[confirmStart..confirmEnd];

        Assert.True(
            confirmFlow.IndexOf("completedPreparation = prepared", StringComparison.Ordinal)
            < confirmFlow.IndexOf("AssignManualPlaceAsync", StringComparison.Ordinal));
        Assert.True(
            confirmFlow.IndexOf("AssignManualPlaceAsync", StringComparison.Ordinal)
            < confirmFlow.IndexOf("SupplementRawLocationsAsync", StringComparison.Ordinal));
        Assert.DoesNotContain("ReclassifyMediaAsync", confirmFlow, StringComparison.Ordinal);

        var createStart = pending.IndexOf("private async Task<PreparedPlace?> PrepareNewPlaceAsync", StringComparison.Ordinal);
        var createEnd = pending.IndexOf("private async Task<bool> ConfirmRadiusExpansionAsync", createStart, StringComparison.Ordinal);
        Assert.True(createStart >= 0);
        Assert.True(createEnd > createStart);
        var createFlow = pending[createStart..createEnd];

        Assert.True(
            createFlow.IndexOf("CreatePlaceAsync", StringComparison.Ordinal)
            < createFlow.IndexOf("ReclassifyMediaAsync", StringComparison.Ordinal));
        Assert.Contains("ReclassificationPerformed: true", createFlow, StringComparison.Ordinal);

        var manualStart = pending.IndexOf("private async Task<AssignMediaPlaceResult> AssignManualPlaceAsync", StringComparison.Ordinal);
        var manualEnd = pending.IndexOf("private async Task<PlaceRevisionRefresh> LoadLatestPlaceRevisionsAsync", manualStart, StringComparison.Ordinal);
        Assert.True(manualStart >= 0);
        Assert.True(manualEnd > manualStart);
        var manualFlow = pending[manualStart..manualEnd];

        Assert.Contains("LoadLatestPlaceRevisionsAsync", manualFlow, StringComparison.Ordinal);
        Assert.Contains("_pendingMemoryService.AssignPlaceAsync", manualFlow, StringComparison.Ordinal);
        Assert.True(
            manualFlow.IndexOf("LoadLatestPlaceRevisionsAsync", StringComparison.Ordinal)
            < manualFlow.IndexOf("_pendingMemoryService.AssignPlaceAsync", StringComparison.Ordinal));
        Assert.Contains("ExpectedPlaceRevisions = latestRevisions", manualFlow, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryLocationKey_IsPreservedAsOpaqueLeafIdentityAndPlaceIdFallbackRemains()
    {
        var dto = File.ReadAllText(FindSourceFile("MemoryKeeper.Application", "DTOs", "FastGalleryDtos.cs"));
        var treeNode = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Models", "GalleryTreeNode.cs"));
        var gallery = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs"));
        var repository = File.ReadAllText(FindSourceFile("MemoryKeeper.Infrastructure", "Repositories", "Api", "FastGalleryApiRepository.cs"));

        Assert.Contains("[JsonPropertyName(\"location_key\")] public string? LocationKey", dto, StringComparison.Ordinal);
        Assert.Contains("public string? LocationKey { get; init; }", treeNode, StringComparison.Ordinal);
        Assert.Contains("return $\"location:{LocationKey}\";", treeNode, StringComparison.Ordinal);
        Assert.Contains("EscapeKeyPart(Title)", treeNode, StringComparison.Ordinal);
        Assert.True(CountOccurrences(gallery, "LocationKey = place.LocationKey") >= 2);
        Assert.Contains("LocationKey = locationKey", gallery, StringComparison.Ordinal);
        Assert.Contains("PlaceId = isPlaceLeaf && locationKey is null ? node.PlaceId : null", gallery, StringComparison.Ordinal);
        Assert.Contains("[\"location_key\"] = query.LocationKey", repository, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(query.LocationKey)", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("registered:v1:", gallery, StringComparison.Ordinal);
        Assert.DoesNotContain("raw:v1:", gallery, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryLocationKey_PersistsAcrossPaginationAndLeafCountRemainsAuthoritative()
    {
        var gallery = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs"));

        Assert.Contains("ToFastQuery(_pagingNode, cursor, exactRegion)", gallery, StringComparison.Ordinal);
        Assert.Contains("StatusMessage = BuildFastStatusMessage(node, galleryItems.Count)", gallery, StringComparison.Ordinal);
        Assert.Contains("StatusMessage = BuildFastStatusMessage(_pagingNode, Items.Count)", gallery, StringComparison.Ordinal);
        Assert.Contains("var displayCount = IsHierarchyPlaceLeaf(node) || node.Kind == GalleryTreeNodeKind.City", gallery, StringComparison.Ordinal);
        Assert.Contains("$\"{node.Title} · {galleryItems.Count}/{_totalCount}장\"", gallery, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryUnclassifiedNode_UsesSingleAuthoritativeFastQueryAcrossPagination()
    {
        var dto = File.ReadAllText(FindSourceFile("MemoryKeeper.Application", "DTOs", "FastGalleryDtos.cs"));
        var viewModel = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs"));
        var repository = File.ReadAllText(FindSourceFile("MemoryKeeper.Infrastructure", "Repositories", "Api", "FastGalleryApiRepository.cs"));
        var loadInitialStart = viewModel.IndexOf("private async Task<FastPageLoadResult> LoadInitialFastPageAsync", StringComparison.Ordinal);
        var loadMoreStart = viewModel.IndexOf("private async Task LoadMoreAsync()", StringComparison.Ordinal);
        var toFastQueryStart = viewModel.IndexOf("private FastGalleryPhotoQuery ToFastQuery", StringComparison.Ordinal);
        var toFastQueryEnd = viewModel.IndexOf("private static string BuildFastStatusMessage", toFastQueryStart, StringComparison.Ordinal);
        Assert.True(loadInitialStart >= 0 && loadMoreStart >= 0 && toFastQueryEnd > toFastQueryStart);
        var toFastQuery = viewModel[toFastQueryStart..toFastQueryEnd];

        Assert.Contains("public bool? Unclassified { get; init; }", dto, StringComparison.Ordinal);
        Assert.Contains("Unclassified = node.Kind == GalleryTreeNodeKind.Unclassified ? true : null", toFastQuery, StringComparison.Ordinal);
        Assert.Contains("Year = node.Year", toFastQuery, StringComparison.Ordinal);
        Assert.Contains("PlaceId = isPlaceLeaf && locationKey is null ? node.PlaceId : null", toFastQuery, StringComparison.Ordinal);
        Assert.Contains("[\"unclassified\"] = query.Unclassified == true ? \"true\" : null", repository, StringComparison.Ordinal);
        Assert.Contains("ToFastQuery(node, regionOverride: exactRegion)", viewModel[loadInitialStart..toFastQueryStart], StringComparison.Ordinal);
        Assert.Contains("ToFastQuery(_pagingNode, cursor, exactRegion)", viewModel[loadMoreStart..loadInitialStart], StringComparison.Ordinal);
        Assert.Contains("node.Kind != GalleryTreeNodeKind.City || node.RegionFilters.Count <= 1", viewModel[loadInitialStart..toFastQueryStart], StringComparison.Ordinal);
        Assert.DoesNotContain("Unclassified = false", toFastQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("Unclassified = true", viewModel[loadInitialStart..toFastQueryStart], StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryPlaceChange_RefreshesTheCachedHierarchyThroughTheExistingDetailEvent()
    {
        var page = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs"));
        var gallery = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs"));

        Assert.Contains("PlaceRegistered += OnDetailPlaceRegistered", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.CaptureFocusState", page, StringComparison.Ordinal);
        Assert.Contains("_catalogInvalidation.Consume(CatalogSurface.Gallery)", page, StringComparison.Ordinal);
        Assert.Contains("await ViewModel.LoadCommand.ExecuteAsync(null)", page, StringComparison.Ordinal);
        Assert.Contains("_catalogInvalidation.Invalidate(CatalogSurface.Gallery)", page, StringComparison.Ordinal);
        Assert.Contains("_fastHierarchy = null;", gallery, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryBatchEdit_UsesNativeSelectionProminentOverlayAndNoDeleteAction()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml"));
        var page = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs"));
        var viewModel = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs"));

        Assert.Contains("Content=\"편집\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"편집 종료\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"전체 선택\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"장소 변경\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"PhotoGrid_OnSelectionChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PhotoGrid.SelectionMode = ListViewSelectionMode.Multiple", page, StringComparison.Ordinal);
        Assert.Contains("PhotoGrid.SelectedItems", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EditSelectionVisual\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"EditSelectionVisual.Visibility\" Value=\"Visible\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PointerOverSelected\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PressedSelected\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{ThemeResource MkBrushPrimary}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{ThemeResource MkBrushPrimary}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"3\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"0.12\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"28\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"14\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE73E;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{ThemeResource MkBrushTextOnPrimary}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"Collapsed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"64\" Height=\"64\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.ColumnSpan=\"3\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsMutating", xaml, StringComparison.Ordinal);
        Assert.Contains("MutationStatus", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEditing", viewModel, StringComparison.Ordinal);
        Assert.Contains("SelectedCount", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"삭제\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryBatchEdit_PreservesLoadedSelectionAndTargetsAuthoritativeRegisteredNode()
    {
        var page = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs"));
        var viewModel = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs"));

        Assert.Contains("foreach (var item in ViewModel.Items)", page, StringComparison.Ordinal);
        Assert.Contains("Items_OnCollectionChanged", page, StringComparison.Ordinal);
        Assert.Contains("ReloadAndSelectRegisteredPlaceAsync", page, StringComparison.Ordinal);
        Assert.Contains("node.PlaceId == placeId", viewModel, StringComparison.Ordinal);
        Assert.Contains("await SelectNodeAsync(target)", viewModel, StringComparison.Ordinal);
        Assert.Contains("_fastHierarchy = null", viewModel, StringComparison.Ordinal);
        Assert.Contains("LoadNextRegionPageAsync", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void GallerySelectionLifecycle_DoesNotDirectlyClearWinUiSelectionVector()
    {
        var xaml = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml"));
        var page = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs"));
        var viewModel = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs"));
        var selectionPolicy = File.ReadAllText(FindSourceFile("MemoryKeeper.Application", "GallerySelectionPolicy.cs"));
        var handlerStart = page.IndexOf("private void ViewModel_OnPropertyChanged", StringComparison.Ordinal);
        var handlerEnd = page.IndexOf("private void ResubscribeItems", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);
        var itemsChangedHandler = page[handlerStart..handlerEnd];
        var enterStart = page.IndexOf("private async void EnterEditMode_OnClick", StringComparison.Ordinal);
        var exitStart = page.IndexOf("private void ExitEditMode_OnClick", enterStart, StringComparison.Ordinal);
        var selectAllStart = page.IndexOf("private void SelectAll_OnClick", exitStart, StringComparison.Ordinal);
        var changePlaceStart = page.IndexOf("private async void ChangePlace_OnClick", selectAllStart, StringComparison.Ordinal);
        var changePlaceEnd = page.IndexOf("private Task<bool> ShowRadiusExpansionPreviewAsync", changePlaceStart, StringComparison.Ordinal);
        Assert.True(enterStart >= 0 && exitStart > enterStart && selectAllStart > exitStart);
        Assert.True(changePlaceStart > selectAllStart && changePlaceEnd > changePlaceStart);
        var enterHandler = page[enterStart..exitStart];
        var exitHandler = page[exitStart..selectAllStart];
        var changePlaceHandler = page[changePlaceStart..changePlaceEnd];
        var selectionChangedStart = page.IndexOf("private void PhotoGrid_OnSelectionChanged", selectAllStart, StringComparison.Ordinal);
        Assert.True(selectionChangedStart > selectAllStart && changePlaceStart > selectionChangedStart);
        var selectionChangedHandler = page[selectionChangedStart..changePlaceStart];
        var selectAllHandler = page[selectAllStart..selectionChangedStart];

        Assert.Contains("SelectionMode=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectAllButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResetSelectionForItemsReplacement", itemsChangedHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItems.Clear", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearNativeSelection", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.EnterEditMode();", enterHandler, StringComparison.Ordinal);
        Assert.Contains("PhotoGrid.SelectionMode = ListViewSelectionMode.Multiple;", enterHandler, StringComparison.Ordinal);
        Assert.True(
            enterHandler.IndexOf("ViewModel.EnterEditMode();", StringComparison.Ordinal)
            < enterHandler.IndexOf("PhotoGrid.SelectionMode = ListViewSelectionMode.Multiple;", StringComparison.Ordinal));
        Assert.Contains("PhotoGrid.SelectionMode = ListViewSelectionMode.None;", exitHandler, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ExitEditMode();", exitHandler, StringComparison.Ordinal);
        Assert.True(
            exitHandler.IndexOf("PhotoGrid.SelectionMode = ListViewSelectionMode.None;", StringComparison.Ordinal)
            < exitHandler.IndexOf("ViewModel.ExitEditMode();", StringComparison.Ordinal));
        Assert.Contains("await ViewModel.ReloadAndSelectRegisteredPlaceAsync(targetId);", changePlaceHandler, StringComparison.Ordinal);
        Assert.Contains("PhotoGrid.SelectionMode = ListViewSelectionMode.None;", changePlaceHandler, StringComparison.Ordinal);
        Assert.Contains("ViewModel.CompleteMutation(true", changePlaceHandler, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ExitEditMode();", changePlaceHandler, StringComparison.Ordinal);
        Assert.True(
            changePlaceHandler.IndexOf("PhotoGrid.SelectionMode = ListViewSelectionMode.None;", StringComparison.Ordinal)
            < changePlaceHandler.IndexOf("ViewModel.ExitEditMode();", StringComparison.Ordinal));
        Assert.Contains("ViewModel.IsEditing && !ViewModel.IsMutating", selectionChangedHandler, StringComparison.Ordinal);
        Assert.Contains("ViewModel.SelectedCount = PhotoGrid.SelectedItems.Count", selectionChangedHandler, StringComparison.Ordinal);
        Assert.Contains("GallerySelectionPolicy.AreAllLoadedItemsSelected", selectAllHandler, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Items.Count", selectAllHandler, StringComparison.Ordinal);
        Assert.Contains("ViewModel.SelectedCount", selectAllHandler, StringComparison.Ordinal);
        Assert.Contains("PhotoGrid.SelectedItems.Remove(item)", selectAllHandler, StringComparison.Ordinal);
        Assert.Contains("PhotoGrid.SelectedItems.Add(item)", selectAllHandler, StringComparison.Ordinal);
        Assert.Contains("UpdateSelectAllButtonContent();", selectAllHandler, StringComparison.Ordinal);
        Assert.Contains("GallerySelectionPolicy.GetToggleLabel", page, StringComparison.Ordinal);
        Assert.Contains("loadedCount > 0 && selectedCount == loadedCount", selectionPolicy, StringComparison.Ordinal);
        Assert.Contains("ClearAllLabel = \"전체 해제\"", selectionPolicy, StringComparison.Ordinal);
        Assert.Contains("SelectAllLabel = \"전체 선택\"", selectionPolicy, StringComparison.Ordinal);

        var viewModelEnterStart = viewModel.IndexOf("public void EnterEditMode()", StringComparison.Ordinal);
        var viewModelExitStart = viewModel.IndexOf("public void ExitEditMode()", viewModelEnterStart, StringComparison.Ordinal);
        var viewModelBeginMutationStart = viewModel.IndexOf("public void BeginMutation", viewModelExitStart, StringComparison.Ordinal);
        var replacementResetStart = viewModel.IndexOf("public void ResetSelectionForItemsReplacement()", StringComparison.Ordinal);
        var replacementResetEnd = viewModel.IndexOf("[RelayCommand]", replacementResetStart, StringComparison.Ordinal);
        Assert.True(viewModelEnterStart >= 0 && viewModelExitStart > viewModelEnterStart);
        Assert.True(viewModelBeginMutationStart > viewModelExitStart);
        Assert.True(replacementResetStart >= 0 && replacementResetEnd > replacementResetStart);
        Assert.Contains("SelectedCount = 0", viewModel[viewModelEnterStart..viewModelExitStart], StringComparison.Ordinal);
        Assert.Contains("SelectedCount = 0", viewModel[viewModelExitStart..viewModelBeginMutationStart], StringComparison.Ordinal);
        Assert.Contains("SelectedCount = 0", viewModel[replacementResetStart..replacementResetEnd], StringComparison.Ordinal);
        Assert.Contains("ClearDisplaySelection();", viewModel[replacementResetStart..replacementResetEnd], StringComparison.Ordinal);

        var collectionChangedStart = page.IndexOf("private void Items_OnCollectionChanged", StringComparison.Ordinal);
        var collectionChangedEnd = page.IndexOf("private void UpdateEmptyState", collectionChangedStart, StringComparison.Ordinal);
        Assert.True(collectionChangedStart >= 0 && collectionChangedEnd > collectionChangedStart);
        Assert.DoesNotContain("SelectionMode", page[collectionChangedStart..collectionChangedEnd], StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedCount", page[collectionChangedStart..collectionChangedEnd], StringComparison.Ordinal);

        var loadMoreStart = viewModel.IndexOf("private async Task LoadMoreAsync()", StringComparison.Ordinal);
        var loadMoreEnd = viewModel.IndexOf("private FastGalleryPhotoQuery ToFastQuery", loadMoreStart, StringComparison.Ordinal);
        Assert.True(loadMoreStart >= 0 && loadMoreEnd > loadMoreStart);
        var loadMoreMethod = viewModel[loadMoreStart..loadMoreEnd];
        Assert.Contains("Items.Add(item)", loadMoreMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionMode", loadMoreMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedCount", loadMoreMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItems", loadMoreMethod, StringComparison.Ordinal);

        Assert.Contains("RestoreNativeSelectionAfterItemsReplacementAsync", page, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue", page, StringComparison.Ordinal);
        var restoreStart = page.IndexOf("private Task RestoreNativeSelectionAfterItemsReplacementAsync", StringComparison.Ordinal);
        Assert.True(restoreStart >= 0);
        Assert.DoesNotContain("Task.Delay", page[restoreStart..], StringComparison.Ordinal);
        Assert.DoesNotContain("COMException", page, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryMutationProgress_DispatchesUiBoundStatusWithoutSuppressingFailures()
    {
        var page = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs"));
        var session = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryPlaceEditSessionViewModel.cs"));
        var start = page.IndexOf("private void UpdateMutationProgressOnUiThread", StringComparison.Ordinal);
        var end = page.IndexOf("private Task ShowMapPickInPlaceDialogAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = page[start..end];

        Assert.Contains("MutationProgress = UpdateMutationProgressOnUiThread", page, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.HasThreadAccess", method, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue", method, StringComparison.Ordinal);
        Assert.Contains("ViewModel.UpdateMutationStatus", method, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException", method, StringComparison.Ordinal);
        Assert.DoesNotContain("COMException", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", method, StringComparison.Ordinal);
        Assert.Contains("stage == GalleryPlaceAssignmentStage.VerifyQuery", session, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryYearHierarchy_UsesCanonicalRegionNodesAndQueriesEveryExactSource()
    {
        var viewModel = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryViewModel.cs"));
        var treeNode = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Models", "GalleryTreeNode.cs"));
        var repository = File.ReadAllText(FindSourceFile("MemoryKeeper.Infrastructure", "Repositories", "Api", "FastGalleryApiRepository.cs"));

        Assert.Contains("GalleryRegionHierarchyProjection.Build(cities)", viewModel, StringComparison.Ordinal);
        Assert.Contains("CanonicalRegion = city.CanonicalIdentity", viewModel, StringComparison.Ordinal);
        Assert.Contains("RegionFilters = city.SourceRegions", viewModel, StringComparison.Ordinal);
        Assert.Contains("Count = city.PhotoCount", viewModel, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll(sourceRegions.Select", viewModel, StringComparison.Ordinal);
        Assert.Contains("ToFastQuery(node, regionOverride: region)", viewModel, StringComparison.Ordinal);
        Assert.Contains("RegionPagingState", viewModel, StringComparison.Ordinal);
        Assert.Contains("LoadNextRegionPageAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("DistinctBy(item => item.FileId", viewModel, StringComparison.Ordinal);
        Assert.Contains("_photoNavigationState.SetPlaylist(Items.Select", viewModel, StringComparison.Ordinal);
        Assert.Contains("City = node.City", viewModel, StringComparison.Ordinal);
        Assert.Contains("RegionFilters = string.IsNullOrWhiteSpace(regionNode.Region)", viewModel, StringComparison.Ordinal);
        Assert.Contains("LocationKey = place.LocationKey", viewModel, StringComparison.Ordinal);
        Assert.Contains("CanonicalRegion ?? City", treeNode, StringComparison.Ordinal);
        Assert.Contains("RegionFilters", treeNode, StringComparison.Ordinal);
        Assert.Contains("[\"region\"] = query.Region", repository, StringComparison.Ordinal);
        Assert.Contains("[\"location_key\"] = query.LocationKey", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("PatchMetadataAsync", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("if (city == \"Osaka\")", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryBatchWorkflow_UsesGenericAtomicApiWithoutRawMetadataOrPendingFallback()
    {
        var workflow = File.ReadAllText(FindSourceFile("MemoryKeeper.Application", "Services", "GalleryPlaceAssignmentWorkflow.cs"));
        var session = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryPlaceEditSessionViewModel.cs"));

        Assert.Contains("QueryFilePlaceStatesAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("AssignFilePlacesAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("ExpectedPlaceRevisions", workflow, StringComparison.Ordinal);
        Assert.Contains("\"initial-state-query\"", workflow, StringComparison.Ordinal);
        Assert.Contains("\"radius-update\"", workflow, StringComparison.Ordinal);
        Assert.Contains("\"revision-refresh\"", workflow, StringComparison.Ordinal);
        Assert.Contains("\"assign\"", workflow, StringComparison.Ordinal);
        Assert.Contains("\"invalidate\"", workflow, StringComparison.Ordinal);
        Assert.Contains("\"verify-query\"", workflow, StringComparison.Ordinal);
        Assert.Contains("\"verify-match\"", workflow, StringComparison.Ordinal);
        Assert.Contains("\"completed\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Gallery place edit stage completed", workflow, StringComparison.Ordinal);
        Assert.Contains("Gallery place edit stage failed", workflow, StringComparison.Ordinal);
        Assert.Contains("ReturnedCount={ReturnedCount}", workflow, StringComparison.Ordinal);
        Assert.Contains("RevisionMapCount={RevisionMapCount}", workflow, StringComparison.Ordinal);
        Assert.Contains("VerifiedCount={VerifiedCount}", workflow, StringComparison.Ordinal);
        Assert.Contains("MismatchCount={MismatchCount}", workflow, StringComparison.Ordinal);
        Assert.Contains("StatusCode={StatusCode}", session, StringComparison.Ordinal);
        Assert.Contains("DetailCode={DetailCode}", session, StringComparison.Ordinal);
        Assert.DoesNotContain("ServerMessage", session, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RequestBody", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ResponseBody", workflow, StringComparison.Ordinal);
        Assert.Contains("PlanRadius", session, StringComparison.Ordinal);
        Assert.Contains("reassignFromOtherPlaces: false", workflow, StringComparison.Ordinal);
        Assert.Contains("ReclassifyMedia = false", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("AssignPendingPlaceAsync", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("PatchMetadataAsync", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("SetRawLocationAsync", session, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryHandledAssignmentFailure_UsesPersistentSafeGalleryDiagnostic()
    {
        var diagnostics = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Diagnostics", "GalleryDiagnostics.cs"));
        var session = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryPlaceEditSessionViewModel.cs"));
        var app = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "App.xaml.cs"));

        Assert.Contains("WriteOperationFailure", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Operation:", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Stage:", diagnostics, StringComparison.Ordinal);
        Assert.Contains("SelectedCount:", diagnostics, StringComparison.Ordinal);
        Assert.Contains("TargetPlaceId:", diagnostics, StringComparison.Ordinal);
        Assert.Contains("ReturnedCount", diagnostics, StringComparison.Ordinal);
        Assert.Contains("RevisionMapCount", diagnostics, StringComparison.Ordinal);
        Assert.Contains("VerifiedCount", diagnostics, StringComparison.Ordinal);
        Assert.Contains("MismatchCount", diagnostics, StringComparison.Ordinal);
        Assert.Contains("HttpStatus:", diagnostics, StringComparison.Ordinal);
        Assert.Contains("DetailCode:", diagnostics, StringComparison.Ordinal);
        Assert.Contains("ExceptionType:", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Message:", diagnostics, StringComparison.Ordinal);
        Assert.Contains("StackTrace:", diagnostics, StringComparison.Ordinal);
        Assert.Contains("NormalizeMultiline(safeStackTrace, 4096)", diagnostics, StringComparison.Ordinal);
        Assert.Contains("WriteLine(builder.ToString().TrimEnd())", diagnostics, StringComparison.Ordinal);
        Assert.Contains("StartupDiagnostics.WriteStep", diagnostics, StringComparison.Ordinal);

        Assert.Contains("operation: \"GalleryPlaceAssignment\"", session, StringComparison.Ordinal);
        Assert.Contains("stage: _diagnosticStage", session, StringComparison.Ordinal);
        Assert.Contains("selectedCount: _selectedItems.Count", session, StringComparison.Ordinal);
        Assert.Contains("targetPlaceId: TargetPlaceId", session, StringComparison.Ordinal);
        Assert.Contains("httpStatus: apiException is null ? null : (int)apiException.StatusCode", session, StringComparison.Ordinal);
        Assert.Contains("detailCode: apiException?.DetailCode", session, StringComparison.Ordinal);
        Assert.Contains("WriteOperationFailureDiagnostic(ex)", session, StringComparison.Ordinal);
        Assert.Contains("new System.Diagnostics.StackTrace(exception, fNeedFileInfo: false)", session, StringComparison.Ordinal);
        Assert.Contains("장소 범위는 변경되었지만 사진의 장소를 저장하지 못했습니다.", session, StringComparison.Ordinal);
        Assert.DoesNotContain("ServerMessage", session, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RequestBody", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("ResponseBody", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("FileIds", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("Gps", diagnostics, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("UnhandledException += OnUnhandledException", app, StringComparison.Ordinal);
        Assert.Contains("ErrorDialog.Show(", app, StringComparison.Ordinal);
        Assert.Contains("stage: \"App.UnhandledException\"", app, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceRegistrationDialog_KeepsSelectionHeaderAndActionsOutsidePickerScroll()
    {
        var dialog = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "Dialogs", "PlaceRegistrationDialog.cs"));
        var scrollStart = dialog.IndexOf("var scrollChildren = new List<UIElement>", StringComparison.Ordinal);
        var scrollEnd = dialog.IndexOf("var scrollContent = new StackPanel", scrollStart, StringComparison.Ordinal);
        Assert.True(scrollStart >= 0 && scrollEnd > scrollStart);
        var scrollContent = dialog[scrollStart..scrollEnd];

        Assert.Contains("fixedHeaderPanel.Children.Add(headerGrid)", dialog, StringComparison.Ordinal);
        Assert.Contains("fixedHeaderPanel.Children.Add(previewCardHost)", dialog, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(fixedHeaderPanel, 0)", dialog, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(scrollViewer, 1)", dialog, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(footer, 2)", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("previewCardHost", scrollContent, StringComparison.Ordinal);
        Assert.Contains("CreateSectionLabel(\"최근 사용 장소\")", scrollContent, StringComparison.Ordinal);
        Assert.Contains("CreateSectionLabel(\"즐겨찾기 장소\")", scrollContent, StringComparison.Ordinal);
        Assert.Contains("existingSearchBox", scrollContent, StringComparison.Ordinal);
        Assert.Contains("CreateGoogleSearchHeader", scrollContent, StringComparison.Ordinal);
        Assert.Contains("googleSearchBox", scrollContent, StringComparison.Ordinal);
        Assert.Contains("nearbyList", scrollContent, StringComparison.Ordinal);
        Assert.Contains("RegistrationPreviewImage", dialog, StringComparison.Ordinal);
        Assert.Contains("RegistrationPreviewFileName", dialog, StringComparison.Ordinal);
        Assert.Contains("BuildPreviewCard(viewModel.SelectedLocation)", dialog, StringComparison.Ordinal);
        Assert.Contains("viewModel.PlacePreviewChanged += OnPreviewChanged", dialog, StringComparison.Ordinal);
        Assert.Contains("Content = \"지도에서 직접 선택\"", dialog, StringComparison.Ordinal);
        Assert.Contains("Text = \"📍 선택한 장소\"", dialog, StringComparison.Ordinal);
        Assert.Contains("선택한 장소가 없습니다.", dialog, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = options.PrimaryButtonText", dialog, StringComparison.Ordinal);
        Assert.Contains("CloseButtonText = \"취소\"", dialog, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string FindSourceFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Source file was not found: {Path.Combine(parts)}");
    }
}
