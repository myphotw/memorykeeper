using MemoryKeeper.Mobile.Controls;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MobileViewerImagePolicyTests
{
    [Fact]
    public void GalleryAndViewer_UseThumbnailAndPreviewWithoutFallbackImages()
    {
        var viewModel = ReadSource("MemoryKeeper.Mobile", "ViewModels", "HomeViewModel.cs");
        var item = ReadSource("MemoryKeeper.Mobile", "Models", "MobileGalleryItem.cs");
        var xaml = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml");
        var thumbnailLoader = ReadSource("MemoryKeeper.Mobile", "Images", "MobileThumbnailSourceFactory.cs");
        var previewLoader = ReadSource("MemoryKeeper.Mobile", "Images", "MobilePreviewSourceFactory.cs");
        var program = ReadSource("MemoryKeeper.Mobile", "MauiProgram.cs");

        Assert.Contains(
            "ThumbnailSource = _thumbnailSourceFactory.Create(photo.FileId, photo.ThumbnailUrl)",
            viewModel,
            StringComparison.Ordinal);
        Assert.Contains("PreviewUrl = photo.PreviewUrl", viewModel, StringComparison.Ordinal);
        Assert.Contains("public string? PreviewUrl", item, StringComparison.Ordinal);
        Assert.Contains("Path=OpenViewerCommand", xaml, StringComparison.Ordinal);
        Assert.Contains(
            ".LoadAsync(item.FileId, item.PreviewUrl, cancellation.Token)",
            viewModel,
            StringComparison.Ordinal);
        Assert.Contains("PreviewBytes=\"{Binding ViewerPreviewBytes}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsRunning=\"{Binding IsPreviewLoading}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsViewerErrorVisible}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BackendMediaUrlResolver.ResolvePreviewUrl", previewLoader, StringComparison.Ordinal);
        Assert.Contains("_endpointResolver.ResolveAsync", previewLoader, StringComparison.Ordinal);
        Assert.Matches(@"_endpointResolver\s*\.ResolveAsync\(cancellationToken\)", thumbnailLoader);
        Assert.Contains("BackendMediaUrlResolver.ToAbsoluteUrl", thumbnailLoader, StringComparison.Ordinal);
        Assert.Contains("MobileHttpClientNames.Backend", previewLoader, StringComparison.Ordinal);
        Assert.Contains(
            "AddSingleton<IMobilePreviewSourceFactory, MobilePreviewSourceFactory>()",
            program,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddHandler<MobileZoomablePreviewView, MobileZoomablePreviewViewHandler>()",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain("OriginalUrl", viewModel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/original", previewLoader, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("thumbnail", previewLoader, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BoundedThumbnailMemoryCache", previewLoader, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryLoadMore_PreloadsEntirePageThenAppendsWithoutResetOrViewerChanges()
    {
        var viewModel = ReadSource("MemoryKeeper.Mobile", "ViewModels", "HomeViewModel.cs");
        var homePage = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml");
        var thumbnailContract = ReadSource(
            "MemoryKeeper.Mobile", "Images", "IMobileThumbnailSourceFactory.cs");
        var thumbnailLoader = ReadSource(
            "MemoryKeeper.Mobile", "Images", "MobileThumbnailSourceFactory.cs");
        var previewLoader = ReadSource(
            "MemoryKeeper.Mobile", "Images", "MobilePreviewSourceFactory.cs");
        var cache = ReadSource(
            "MemoryKeeper.Application", "Services", "BoundedThumbnailMemoryCache.cs");
        var loadMore = Slice(
            viewModel,
            "public async Task LoadMoreAsync()",
            "public Task RetryAsync()");
        var firstPage = Slice(
            viewModel,
            "private async Task LoadFirstPageAsync(",
            "private async Task LoadBrowseOptionsSafelyAsync()");
        var createThumbnail = Slice(
            thumbnailLoader,
            "public ImageSource? Create(",
            "public async Task PreloadAsync(");
        var preloadThumbnail = Slice(
            thumbnailLoader,
            "public async Task PreloadAsync(",
            "private async Task<Stream> OpenThumbnailAsync(");
        var openThumbnail = Slice(
            thumbnailLoader,
            "private async Task<Stream> OpenThumbnailAsync(",
            "private async Task<byte[]?> LoadThumbnailBytesAsync(");
        var loadThumbnailBytes = Slice(
            thumbnailLoader,
            "private async Task<byte[]?> LoadThumbnailBytesAsync(",
            "private static string CreateCacheKey(");
        var preloadPage = Slice(
            viewModel,
            "private Task PreloadLoadMoreThumbnailsAsync(",
            "private void ApplyContinuation(");

        var loadingStartIndex = loadMore.IndexOf("IsLoadingMore = true;", StringComparison.Ordinal);
        var preloadIndex = loadMore.IndexOf("await PreloadLoadMoreThumbnailsAsync(", StringComparison.Ordinal);
        var currentCheckAfterPreload = loadMore.IndexOf(
            "if (!IsCurrentRequest(request))",
            preloadIndex,
            StringComparison.Ordinal);
        var appendLoopIndex = loadMore.IndexOf(
            "foreach (var item in update.AddedItems)",
            StringComparison.Ordinal);
        var appendIndex = loadMore.IndexOf(
            "Items.Add(ToMobileItem(item));",
            appendLoopIndex,
            StringComparison.Ordinal);
        var continuationIndex = loadMore.IndexOf("ApplyContinuation(update);", StringComparison.Ordinal);
        var loadingStopIndex = loadMore.LastIndexOf("IsLoadingMore = false;", StringComparison.Ordinal);

        Assert.Contains(
            "public ObservableCollection<MobileGalleryItem> Items { get; } = [];",
            viewModel,
            StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll(photos", preloadPage, StringComparison.Ordinal);
        Assert.Contains(".Select(photo => _thumbnailSourceFactory.PreloadAsync(", preloadPage, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(", preloadPage, StringComparison.Ordinal);
        Assert.Contains("request.Cancellation.Token", loadMore, StringComparison.Ordinal);
        Assert.True(loadingStartIndex >= 0);
        Assert.True(preloadIndex > loadingStartIndex);
        Assert.True(currentCheckAfterPreload > preloadIndex);
        Assert.True(appendLoopIndex > currentCheckAfterPreload);
        Assert.True(appendIndex > appendLoopIndex);
        Assert.True(continuationIndex > appendIndex);
        Assert.True(loadingStopIndex > continuationIndex);
        Assert.DoesNotContain("Items.Clear()", loadMore, StringComparison.Ordinal);
        Assert.DoesNotContain("Items =", loadMore, StringComparison.Ordinal);
        Assert.DoesNotContain("Items.AddRange(", loadMore, StringComparison.Ordinal);
        Assert.DoesNotContain("Reset", loadMore, StringComparison.Ordinal);
        Assert.Contains("Items.Clear()", firstPage, StringComparison.Ordinal);
        Assert.Contains("Items.Add(item)", firstPage, StringComparison.Ordinal);
        Assert.Contains("Task PreloadAsync(", thumbnailContract, StringComparison.Ordinal);
        Assert.Contains("CreateCacheKey(fileId, thumbnailUrl)", createThumbnail, StringComparison.Ordinal);
        Assert.Contains("CreateCacheKey(fileId, thumbnailUrl)", preloadThumbnail, StringComparison.Ordinal);
        Assert.Contains("LoadThumbnailBytesAsync(", preloadThumbnail, StringComparison.Ordinal);
        Assert.Contains("LoadThumbnailBytesAsync(", openThumbnail, StringComparison.Ordinal);
        Assert.Contains("_cache", loadThumbnailBytes, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException)", loadThumbnailBytes, StringComparison.Ordinal);
        Assert.Contains("throw;", loadThumbnailBytes, StringComparison.Ordinal);
        Assert.Contains("catch", loadThumbnailBytes, StringComparison.Ordinal);
        Assert.Contains("return null;", loadThumbnailBytes, StringComparison.Ordinal);
        Assert.Contains("DefaultMaxConcurrentLoads = 6", cache, StringComparison.Ordinal);
        Assert.Contains("DefaultMaxEntries = 256", cache, StringComparison.Ordinal);
        Assert.Contains("DefaultMaxBytes = 64L * 1024 * 1024", cache, StringComparison.Ordinal);
        Assert.Contains("<CollectionView.Footer>", homePage, StringComparison.Ordinal);
        Assert.Contains("IsRunning=\"{Binding IsLoadingMore}\"", homePage, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsLoadingMore}\"", homePage, StringComparison.Ordinal);
        Assert.DoesNotContain("PreloadAsync", previewLoader, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidGalleryThumbnail_UsesBoundedDecodedSessionCacheWithoutViewerChanges()
    {
        var xaml = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml");
        var program = ReadSource("MemoryKeeper.Mobile", "MauiProgram.cs");
        var source = ReadSource("MemoryKeeper.Mobile", "Images", "MobileThumbnailImageSource.cs");
        var factory = ReadSource("MemoryKeeper.Mobile", "Images", "MobileThumbnailSourceFactory.cs");
        var handler = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Handlers", "MobileThumbnailImageHandler.cs");
        var androidCache = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Images", "AndroidDecodedThumbnailCache.cs");
        var cachePolicy = ReadSource(
            "MemoryKeeper.Application", "Services", "BoundedDecodedThumbnailCache.cs");
        var preview = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Controls", "ZoomablePreviewImageView.cs");
        var cacheHitIndex = handler.IndexOf(
            "cache.TryGet(source.CacheKey",
            StringComparison.Ordinal);
        var cachedBitmapIndex = handler.IndexOf(
            "PlatformView.SetImageBitmap(cached)",
            cacheHitIndex,
            StringComparison.Ordinal);
        var hitReturnIndex = handler.IndexOf(
            "return;",
            cachedBitmapIndex,
            StringComparison.Ordinal);
        var standardLoadIndex = handler.IndexOf(
            "await ImageHandler.MapSourceAsync(this, view)",
            hitReturnIndex,
            StringComparison.Ordinal);

        Assert.Contains("<controls:MobileThumbnailImage", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "AddHandler<MobileThumbnailImage, MobileThumbnailImageHandler>()",
            program,
            StringComparison.Ordinal);
        Assert.Contains("AddSingleton<AndroidDecodedThumbnailCache>()", program, StringComparison.Ordinal);
        Assert.Contains("MobileThumbnailImageSource : StreamImageSource", source, StringComparison.Ordinal);
        Assert.Contains("public required string CacheKey", source, StringComparison.Ordinal);
        Assert.Contains("return new MobileThumbnailImageSource", factory, StringComparison.Ordinal);
        Assert.Contains("CacheKey = cacheKey", factory, StringComparison.Ordinal);
        Assert.Contains("? $\"url:{thumbnailUrl.Trim()}\"", factory, StringComparison.Ordinal);
        Assert.Contains(": $\"file:{fileId.Trim()}\"", factory, StringComparison.Ordinal);
        Assert.True(cacheHitIndex >= 0);
        Assert.True(cachedBitmapIndex > cacheHitIndex);
        Assert.True(hitReturnIndex > cachedBitmapIndex);
        Assert.True(standardLoadIndex > hitReturnIndex);
        Assert.Contains("SourceLoader.Reset()", handler, StringComparison.Ordinal);
        Assert.Contains("cache.AddCopy(source.CacheKey, bitmap)", handler, StringComparison.Ordinal);
        Assert.Contains("DefaultMaxBytes = 48L * 1024 * 1024", cachePolicy, StringComparison.Ordinal);
        Assert.Contains("bitmap.AllocationByteCount", androidCache, StringComparison.Ordinal);
        Assert.Contains("source.GetConfig() ?? Bitmap.Config.Argb8888", androidCache, StringComparison.Ordinal);
        Assert.Contains("source.Copy(copyConfig, false)", androidCache, StringComparison.Ordinal);
        Assert.DoesNotContain(".Recycle(", androidCache, StringComparison.Ordinal);
        Assert.DoesNotContain("BoundedDecodedThumbnailCache", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("AndroidDecodedThumbnailCache", preview, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-240, MobileViewerNavigationDirection.Next)]
    [InlineData(240, MobileViewerNavigationDirection.Previous)]
    public void OneXHorizontalSwipe_RequestsNavigationInGalleryOrder(
        double deltaX,
        MobileViewerNavigationDirection expected)
    {
        var direction = MobileViewerGestureDecision.ResolveSwipe(
            scale: MobileZoomState.MinimumScale,
            hadMultiplePointers: false,
            deltaX,
            deltaY: 12,
            velocityX: Math.Sign(deltaX) * 1000,
            touchSlop: 20,
            minimumFlingVelocity: 50);

        Assert.Equal(expected, direction);
    }

    [Theory]
    [InlineData(2, false, 240, 0, 1000)]
    [InlineData(1, true, 240, 0, 1000)]
    [InlineData(1, false, 30, 0, 1000)]
    [InlineData(1, false, 100, 200, 1000)]
    [InlineData(1, false, 240, 0, 40)]
    public void ZoomPinchSmallOrVerticalGesture_DoesNotNavigate(
        double scale,
        bool hadMultiplePointers,
        double deltaX,
        double deltaY,
        double velocityX)
    {
        var direction = MobileViewerGestureDecision.ResolveSwipe(
            scale,
            hadMultiplePointers,
            deltaX,
            deltaY,
            velocityX,
            touchSlop: 20,
            minimumFlingVelocity: 50);

        Assert.Equal(MobileViewerNavigationDirection.None, direction);
    }

    [Fact]
    public void ViewerPosition_UsesLoadedOrderWithoutWrappingAtEitherBoundary()
    {
        var position = new MobileViewerPosition();

        Assert.True(position.TryOpen(index: 2, itemCount: 5));
        Assert.True(position.TryMove(offset: 1, itemCount: 5, out var next));
        Assert.Equal(3, next);
        Assert.True(position.TryMove(offset: -1, itemCount: 5, out var previous));
        Assert.Equal(2, previous);

        Assert.True(position.TryOpen(index: 0, itemCount: 5));
        Assert.False(position.TryMove(offset: -1, itemCount: 5, out var beforeFirst));
        Assert.Equal(0, beforeFirst);
        Assert.Equal(0, position.CurrentIndex);

        Assert.True(position.TryOpen(index: 4, itemCount: 5));
        Assert.False(position.TryMove(offset: 1, itemCount: 5, out var afterLast));
        Assert.Equal(4, afterLast);
        Assert.Equal(4, position.CurrentIndex);
    }

    [Fact]
    public void ViewerNavigation_KeepsOverlayAndLoadedCollectionOrderWithoutPaging()
    {
        var viewModel = ReadSource("MemoryKeeper.Mobile", "ViewModels", "HomeViewModel.cs");
        var xaml = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml");
        var codeBehind = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml.cs");
        var moveMethod = Slice(
            viewModel,
            "private Task MoveViewerAsync(int offset)",
            "private async Task LoadViewerItemAsync");

        Assert.Contains("Items[targetIndex]", moveMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy", moveMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadMore", moveMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("_paging", moveMethod, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsViewerMode}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ViewerPreviewSurface\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviousRequested=\"OnViewerPreviousRequested\"", xaml, StringComparison.Ordinal);
        Assert.Contains("NextRequested=\"OnViewerNextRequested\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowPreviousViewerItemAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ShowNextViewerItemAsync", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoItem_IsClassifiedAndBypassesPhotoPreviewDecode()
    {
        var viewModel = ReadSource("MemoryKeeper.Mobile", "ViewModels", "HomeViewModel.cs");
        var item = ReadSource("MemoryKeeper.Mobile", "Models", "MobileGalleryItem.cs");
        var xaml = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml");
        var loadMethod = Slice(
            viewModel,
            "private async Task LoadViewerItemAsync(MobileGalleryItem item)",
            "public void MarkViewerPreviewReady");

        Assert.Equal(
            MemoryKeeper.Domain.Enums.MediaType.Video,
            MemoryKeeper.Application.MediaTypeResolver.Resolve("video/mp4", ".mp4", "clip.mp4"));
        Assert.Contains("public bool IsVideo", item, StringComparison.Ordinal);
        Assert.Contains("MediaTypeResolver.Resolve", viewModel, StringComparison.Ordinal);
        Assert.True(
            loadMethod.IndexOf("if (item.IsVideo)", StringComparison.Ordinal)
            < loadMethod.IndexOf("_previewSourceFactory", StringComparison.Ordinal));
        Assert.Contains("IsViewerVideoStateVisible", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewerVideoRequest = new MobileVideoPlaybackRequest", loadMethod, StringComparison.Ordinal);
        Assert.Contains("MobileVideoPlayerView", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("동영상 재생은 준비 중입니다.", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("_previewSourceFactory", Slice(
            loadMethod,
            "if (item.IsVideo)",
            "var previewGeneration = Interlocked.Increment"), StringComparison.Ordinal);
    }

    [Fact]
    public void ViewerItemChange_ResetsTransformAndKeepsPhotoSwipeAvailableDuringPhotoLoading()
    {
        var nativeView = ReadSource(
            "MemoryKeeper.Mobile",
            "Platforms",
            "Android",
            "Controls",
            "ZoomablePreviewImageView.cs");
        var xaml = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml");
        var setPreview = Slice(nativeView, "public void SetPreview(", "public void ReleasePreview()");

        Assert.Contains("ClearBitmap();", setPreview, StringComparison.Ordinal);
        Assert.Contains("_zoomState.Reset();", nativeView, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "InputTransparent=\"{Binding IsPreviewLoading}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IsVisible=\"{Binding IsViewerPreviewVisible}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsVisible=\"{Binding IsViewerPhotoSurfaceVisible}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("InputTransparent=\"True\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidViewer_UsesNativeMatrixGesturePathWithoutNetworkOrSourceChanges()
    {
        var nativeView = ReadSource(
            "MemoryKeeper.Mobile",
            "Platforms",
            "Android",
            "Controls",
            "ZoomablePreviewImageView.cs");
        var handler = ReadSource(
            "MemoryKeeper.Mobile",
            "Platforms",
            "Android",
            "Handlers",
            "MobileZoomablePreviewViewHandler.cs");

        Assert.Contains("ScaleGestureDetector", nativeView, StringComparison.Ordinal);
        Assert.Contains("_zoomState.ApplyScale", nativeView, StringComparison.Ordinal);
        Assert.Contains("_zoomState.PanBy", nativeView, StringComparison.Ordinal);
        Assert.Contains("MobileViewerGestureDecision.ResolveSwipe", nativeView, StringComparison.Ordinal);
        Assert.Contains("_touchSequenceHadMultiplePointers", nativeView, StringComparison.Ordinal);
        Assert.Contains("ImageMatrix = _imageMatrix", nativeView, StringComparison.Ordinal);
        Assert.Contains("MapPreviewBytes", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", nativeView, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAsync", nativeView, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageSource", nativeView, StringComparison.Ordinal);
        Assert.DoesNotContain("Original", nativeView, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ZoomState_RepeatedPinchContinuesFromCurrentScaleAndClampsAtFourX()
    {
        var state = CreateZoomState();

        state.ApplyScale(2, 500, 500);
        state.ApplyScale(1.5, 500, 500);
        Assert.Equal(3, state.Scale, 5);

        state.ApplyScale(2, 500, 500);
        Assert.Equal(MobileZoomState.MaximumScale, state.Scale, 5);
    }

    [Fact]
    public void ZoomState_PanPersistsUntilScaleReturnsToOneX()
    {
        var state = CreateZoomState();
        state.ApplyScale(2, 500, 500);

        state.PanBy(120, -80);

        Assert.Equal(120, state.TranslationX, 5);
        Assert.Equal(-80, state.TranslationY, 5);

        state.ApplyScale(0.5, 500, 500);
        Assert.Equal(1, state.Scale, 5);
        Assert.Equal(0, state.TranslationX, 5);
        Assert.Equal(0, state.TranslationY, 5);
    }

    [Fact]
    public void PreviewDecode_HasExplicitSuccessFailureAndViewerLifetimeBoundaries()
    {
        var nativeView = ReadSource(
            "MemoryKeeper.Mobile",
            "Platforms",
            "Android",
            "Controls",
            "ZoomablePreviewImageView.cs");
        var handler = ReadSource(
            "MemoryKeeper.Mobile",
            "Platforms",
            "Android",
            "Handlers",
            "MobileZoomablePreviewViewHandler.cs");
        var viewModel = ReadSource("MemoryKeeper.Mobile", "ViewModels", "HomeViewModel.cs");

        Assert.Contains("BitmapFactory.DecodeByteArray", nativeView, StringComparison.Ordinal);
        Assert.Contains("PreviewLoaded?.Invoke", nativeView, StringComparison.Ordinal);
        Assert.Contains("PreviewFailed?.Invoke", nativeView, StringComparison.Ordinal);
        Assert.Contains("ViewerPreviewBytes = preview.Bytes", viewModel, StringComparison.Ordinal);
        Assert.Contains("ViewerPreviewBytes = null", viewModel, StringComparison.Ordinal);
        Assert.Contains("_viewerLoadCancellation?.Cancel()", viewModel, StringComparison.Ordinal);
        Assert.Contains("_viewerLoadGeneration", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsCurrentViewerRequest", viewModel, StringComparison.Ordinal);
        Assert.Contains("platformView.ReleasePreview()", handler, StringComparison.Ordinal);
        Assert.Contains("_bitmap?.Dispose()", nativeView, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageSource.FromStream", nativeView, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryStream", nativeView, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewerRequests_RejectCanceledAndStaleHttpOrDecodeCompletion()
    {
        var viewModel = ReadSource("MemoryKeeper.Mobile", "ViewModels", "HomeViewModel.cs");
        var nativeView = ReadSource(
            "MemoryKeeper.Mobile",
            "Platforms",
            "Android",
            "Controls",
            "ZoomablePreviewImageView.cs");

        Assert.Contains("ReferenceEquals(_viewerLoadCancellation, cancellation)", viewModel, StringComparison.Ordinal);
        Assert.Contains("generation == Volatile.Read(ref _viewerLoadGeneration)", viewModel, StringComparison.Ordinal);
        Assert.Contains("!cancellation.IsCancellationRequested", viewModel, StringComparison.Ordinal);
        Assert.Contains("generation == Volatile.Read(ref _decodeGeneration)", nativeView, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_decodeCancellation, cancellation)", nativeView, StringComparison.Ordinal);
        Assert.Contains("decoded?.Dispose()", nativeView, StringComparison.Ordinal);
    }

    private static MobileZoomState CreateZoomState()
    {
        var state = new MobileZoomState();
        state.SetViewport(1000, 1000);
        state.SetImageSize(1000, 1000);
        return state;
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker was not found: {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker was not found after start: {endMarker}");
        return source[start..end];
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Source file was not found: {Path.Combine(parts)}");
    }
}
