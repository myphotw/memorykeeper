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

        Assert.Contains("_fastGallery.GetPhotosAsync(ToFastQuery(_pagingNode, cursor))", gallery, StringComparison.Ordinal);
        Assert.Contains("StatusMessage = BuildFastStatusMessage(node, galleryItems.Count)", gallery, StringComparison.Ordinal);
        Assert.Contains("StatusMessage = BuildFastStatusMessage(_pagingNode, Items.Count)", gallery, StringComparison.Ordinal);
        Assert.Contains("var displayCount = IsHierarchyPlaceLeaf(node) ? node.Count : loadedCount", gallery, StringComparison.Ordinal);
        Assert.Contains("$\"{node.Title} · {galleryItems.Count}/{_totalCount}장\"", gallery, StringComparison.Ordinal);
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
        Assert.Contains("_fastGallery.GetPhotosAsync(ToFastQuery(_pagingNode, cursor))", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryBatchWorkflow_UsesGenericAtomicApiWithoutRawMetadataOrPendingFallback()
    {
        var workflow = File.ReadAllText(FindSourceFile("MemoryKeeper.Application", "Services", "GalleryPlaceAssignmentWorkflow.cs"));
        var session = File.ReadAllText(FindSourceFile("MemoryKeeper.App", "ViewModels", "GalleryPlaceEditSessionViewModel.cs"));

        Assert.Contains("QueryFilePlaceStatesAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("AssignFilePlacesAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("ExpectedPlaceRevisions", workflow, StringComparison.Ordinal);
        Assert.Contains("GalleryPlaceAssignmentStage.Verifying", workflow, StringComparison.Ordinal);
        Assert.Contains("PlanRadius", session, StringComparison.Ordinal);
        Assert.Contains("reassignFromOtherPlaces: false", workflow, StringComparison.Ordinal);
        Assert.Contains("ReclassifyMedia = false", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("AssignPendingPlaceAsync", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("PatchMetadataAsync", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("SetRawLocationAsync", session, StringComparison.Ordinal);
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
