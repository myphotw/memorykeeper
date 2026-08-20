using System.Text.Json;
using MemoryKeeper.Application;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace MemoryKeeper.App.Maps.Google;

/// <summary>
/// Google Maps JavaScript API host via WebView2.
/// </summary>
public sealed class GoogleMapController : IMapController, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly WebView2 _webView;
    private readonly ILogger<GoogleMapController> _logger;
    private readonly string _hostFolder;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);
    private bool _coreWired;
    private bool _ready;
    private bool _tilesLoaded;
    private TaskCompletionSource<bool>? _readyTcs;
    private TaskCompletionSource<bool>? _tilesTcs;
    private IReadOnlyList<Guid> _matchedIds = [];
    private string? _lastApiKey;
    private int _htmlLoadGeneration;

    public GoogleMapController(WebView2 webView, ILogger<GoogleMapController> logger)
    {
        _webView = webView;
        _logger = logger;
        _hostFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MemoryKeeper",
            "map-host");
    }

    public bool IsReady => _ready;

    public bool HasTilesLoaded => _tilesLoaded;

    public bool IsHostLayoutReady =>
        _webView.XamlRoot is not null
        && _webView.Visibility == Visibility.Visible
        && _webView.ActualWidth >= 32
        && _webView.ActualHeight >= 32;

    public event EventHandler? Ready;

    public event EventHandler? TilesLoaded;

    public event EventHandler<Guid>? MarkerClicked;

    public event EventHandler<Guid?>? MarkerHovered;

    public event EventHandler<(double Lat, double Lng)>? MapClicked;

    public event EventHandler<(double Lat, double Lng)>? EditableMarkerDragEnded;

    public async Task EnsureMapReadyAsync(
        string? apiKey,
        bool forceReload = false,
        CancellationToken cancellationToken = default)
    {
        await _ensureLock.WaitAsync(cancellationToken);
        try
        {
            // If the map document is already alive, never reload just because the host
            // temporarily shrank (photo panel). Selection must not recreate HTML.
            if (_ready && !forceReload && _webView.CoreWebView2 is not null
                && string.Equals(_lastApiKey, apiKey, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "EnsureMapReady reuse (no reload). Size={Width:0}x{Height:0} LayoutReady={Layout} TilesLoaded={Tiles}",
                    _webView.ActualWidth,
                    _webView.ActualHeight,
                    IsHostLayoutReady,
                    _tilesLoaded);
                if (IsHostLayoutReady)
                {
                    await NotifyLayoutAsync(cancellationToken);
                }

                return;
            }

            _logger.LogInformation(
                "EnsureMapReady reload. Force={Force} WasReady={Ready} LayoutReady={Layout} Size={Width:0}x{Height:0}",
                forceReload,
                _ready,
                IsHostLayoutReady,
                _webView.ActualWidth,
                _webView.ActualHeight);

            try
            {
                await ReloadHtmlAsync(apiKey, cancellationToken);
            }
            catch (Exception ex) when (
                !cancellationToken.IsCancellationRequested
                && GoogleMapsApiKeyValidator.NormalizeOrNull(apiKey) is not null)
            {
                _logger.LogWarning(
                    ex,
                    "Google Maps initialization failed; switching to OpenStreetMap fallback.");
                await ReloadHtmlAsync(apiKey: null, cancellationToken);
                // Reuse the working OSM document for this deployment credential until a
                // caller explicitly requests forceReload.
                _lastApiKey = apiKey;
            }
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    public Task WaitUntilMapReadyAsync(CancellationToken cancellationToken = default) =>
        EnsureReadyAsync(cancellationToken);

    public async Task<bool> WaitUntilTilesLoadedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_tilesLoaded)
        {
            return true;
        }

        var tcs = _tilesTcs;
        if (tcs is null)
        {
            return false;
        }

        try
        {
            await tcs.Task.WaitAsync(timeout, cancellationToken);
            return _tilesLoaded;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    public Task InitializeAsync(string? apiKey, CancellationToken cancellationToken = default) =>
        EnsureMapReadyAsync(apiKey, forceReload: true, cancellationToken);

    public async Task SetMarkersAsync(IReadOnlyList<MapMarker> markers, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        _matchedIds = markers.Select(marker => marker.Id).ToList();
        await PostAsync(new
        {
            type = "setMarkers",
            matchedIds = _matchedIds.Select(id => id.ToString()).ToList(),
            markers = markers.Select(marker => new
            {
                id = marker.Id.ToString(),
                title = marker.Title,
                lat = marker.Latitude,
                lng = marker.Longitude,
                info = marker.Info,
                state = marker.State.ToString().ToLowerInvariant(),
                scale = marker.Scale,
                isFavorite = marker.IsFavorite,
                isMatched = marker.IsMatched
            }).ToList()
        }, cancellationToken);
    }

    public async Task SelectMarkerAsync(
        Guid? placeId,
        bool center = true,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await PostAsync(new
        {
            type = "selectMarker",
            id = placeId?.ToString(),
            center,
            zoom = center ? 15 : (int?)null
        }, cancellationToken);
    }

    public async Task HoverMarkerAsync(Guid? placeId, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await PostAsync(new
        {
            type = "hoverMarker",
            id = placeId?.ToString()
        }, cancellationToken);
    }

    public async Task HighlightMarkersAsync(
        IReadOnlyCollection<Guid> matchedPlaceIds,
        CancellationToken cancellationToken = default)
    {
        _matchedIds = matchedPlaceIds.ToList();
        await EnsureReadyAsync(cancellationToken);
        await PostAsync(new
        {
            type = "highlightMarkers",
            matchedIds = _matchedIds.Select(id => id.ToString()).ToList()
        }, cancellationToken);
    }

    public async Task CenterOnAsync(
        double latitude,
        double longitude,
        int? zoom = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await PostAsync(new
        {
            type = "center",
            lat = latitude,
            lng = longitude,
            zoom
        }, cancellationToken);
    }

    public async Task SetZoomAsync(int zoom, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await PostAsync(new { type = "setZoom", zoom }, cancellationToken);
    }

    public async Task FitMarkersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await PostAsync(new { type = "fitMarkers" }, cancellationToken);
    }

    public async Task ZoomByAsync(int delta, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await PostAsync(new { type = "zoomBy", delta }, cancellationToken);
    }

    public async Task EnableMapClickAsync(bool enabled, CancellationToken ct = default)
    {
        await EnsureReadyAsync(ct);
        await PostAsync(new { type = "enableMapClick", enabled }, ct);
    }

    public async Task SetEditablePinAsync(
        double lat,
        double lng,
        double radiusMeters,
        int zoom = 17,
        CancellationToken ct = default)
    {
        await EnsureReadyAsync(ct);
        await PostAsync(new
        {
            type = "setEditablePin",
            lat,
            lng,
            radiusMeters,
            zoom
        }, ct);
    }

    public async Task UpdateEditableRadiusAsync(double radiusMeters, CancellationToken ct = default)
    {
        await EnsureReadyAsync(ct);
        await PostAsync(new { type = "updateEditableRadius", radiusMeters }, ct);
    }

    public async Task ClearEditablePinAsync(CancellationToken ct = default)
    {
        await EnsureReadyAsync(ct);
        await PostAsync(new { type = "clearEditablePin" }, ct);
    }

    public async Task NotifyLayoutAsync(CancellationToken cancellationToken = default)
    {
        if (!_ready || _webView.CoreWebView2 is null)
        {
            return;
        }

        _logger.LogInformation(
            "layout resize invoked. Size={Width:0}x{Height:0}",
            _webView.ActualWidth,
            _webView.ActualHeight);
        await PostAsync(new { type = "resize" }, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            _webView.CoreWebView2.WebResourceResponseReceived -= OnWebResourceResponseReceived;
        }

        _webView.SizeChanged -= OnWebViewSizeChanged;
        _ensureLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task ReloadHtmlAsync(string? apiKey, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_hostFolder);
        var htmlPath = Path.Combine(_hostFolder, "index.html");
        await File.WriteAllTextAsync(
            htmlPath,
            GoogleMapHtmlBuilder.Build(apiKey),
            cancellationToken);

        await _webView.EnsureCoreWebView2Async();
        WireCoreWebView2Once();

        _ready = false;
        _tilesLoaded = false;
        _lastApiKey = apiKey;
        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tilesTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loadGen = Interlocked.Increment(ref _htmlLoadGeneration);

        _logger.LogInformation(
            "HTML load started. Generation={Gen} Size={Width:0}x{Height:0}",
            loadGen,
            _webView.ActualWidth,
            _webView.ActualHeight);

        var cacheBust = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _webView.Source = new Uri($"https://app.memorykeeper.local/index.html?v={cacheBust}");

        using var registration = cancellationToken.Register(() => _readyTcs.TrySetCanceled(cancellationToken));
        try
        {
            await _readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(25), cancellationToken);
            if (loadGen != _htmlLoadGeneration)
            {
                _logger.LogInformation("stale async result ignored (html load generation mismatch).");
                return;
            }

            _logger.LogInformation("HTML load completed / mapReady received. Generation={Gen}", loadGen);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Google Map initialization timed out.");
            throw new InvalidOperationException(
                "지도를 불러오는 데 시간이 오래 걸리고 있습니다. 네트워크 연결을 확인해 주세요.",
                ex);
        }
    }

    private void WireCoreWebView2Once()
    {
        if (_coreWired)
        {
            return;
        }

        _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webView.CoreWebView2.WebResourceResponseReceived += OnWebResourceResponseReceived;
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.memorykeeper.local",
            _hostFolder,
            CoreWebView2HostResourceAccessKind.Allow);
        _webView.SizeChanged += OnWebViewSizeChanged;
        _coreWired = true;
        _logger.LogInformation("CoreWebView2 ready / wired.");
    }

    private void OnWebViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_ready || e.NewSize.Width < 32 || e.NewSize.Height < 32)
        {
            return;
        }

        _ = NotifyLayoutAsync();
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_ready)
        {
            return;
        }

        if (_readyTcs is null)
        {
            throw new InvalidOperationException("Map controller has not been initialized.");
        }

        await _readyTcs.Task.WaitAsync(cancellationToken);
    }

    private async Task PostAsync(object payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await _webView.DispatcherQueue.EnqueueAsync(() =>
        {
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        });
    }

    private void OnWebResourceResponseReceived(CoreWebView2 sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        try
        {
            var uri = args.Request.Uri;
            if (string.IsNullOrWhiteSpace(uri))
            {
                return;
            }

            var isMapsJs = uri.Contains("maps.googleapis.com/maps/api/js", StringComparison.OrdinalIgnoreCase);
            var isTile = uri.Contains("maps.googleapis.com/maps/vt", StringComparison.OrdinalIgnoreCase)
                         || uri.Contains("maps.gstatic.com", StringComparison.OrdinalIgnoreCase);

            if (!isMapsJs && !isTile)
            {
                return;
            }

            _ = LogResourceAsync(uri, args.Response, isMapsJs);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to inspect Google Maps network response.");
        }
    }

    private async Task LogResourceAsync(string uri, CoreWebView2WebResourceResponseView? response, bool isMapsJs)
    {
        if (response is null)
        {
            return;
        }

        try
        {
            var status = response.StatusCode;
            if (isMapsJs || status >= 400)
            {
                _logger.LogInformation(
                    "Google Maps network. Status={Status} Uri={Uri}",
                    status,
                    TruncateUri(uri));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Google Maps response status read failed. Uri={Uri}", TruncateUri(uri));
        }

        await Task.CompletedTask;
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            if (!document.RootElement.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var type = typeElement.GetString();
            if (type is "ready" or "mapReady")
            {
                LogMapDiagnostics(document.RootElement, "mapReady");
                _ready = true;
                _readyTcs?.TrySetResult(true);
                Ready?.Invoke(this, EventArgs.Empty);
                _ = NotifyLayoutAsync();
            }
            else if (type is "tilesLoaded")
            {
                LogMapDiagnostics(document.RootElement, "tilesLoaded");
                _tilesLoaded = true;
                _tilesTcs?.TrySetResult(true);
                TilesLoaded?.Invoke(this, EventArgs.Empty);
            }
            else if (type is "layout")
            {
                LogMapDiagnostics(document.RootElement, "layout");
            }
            else if (type is "console")
            {
                var level = document.RootElement.TryGetProperty("level", out var levelEl)
                    ? levelEl.GetString()
                    : "info";
                var message = document.RootElement.TryGetProperty("message", out var messageEl)
                    ? messageEl.GetString()
                    : string.Empty;
                if (string.Equals(level, "error", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Google Map console error. Message={Message}", message);
                }
                else
                {
                    _logger.LogInformation("Google Map console {Level}. Message={Message}", level, message);
                }
            }
            else if (type is "error")
            {
                var message = document.RootElement.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : "Unknown map error.";
                _logger.LogWarning("Google Map reported an error. Message={Message}", message);
                _readyTcs?.TrySetException(new InvalidOperationException(message));
            }
            else if (type is "markerClick")
            {
                if (TryReadGuid(document.RootElement, out var id))
                {
                    MarkerClicked?.Invoke(this, id);
                }
            }
            else if (type is "markerHover")
            {
                if (document.RootElement.TryGetProperty("id", out var idElement) &&
                    idElement.ValueKind == JsonValueKind.Null)
                {
                    MarkerHovered?.Invoke(this, null);
                }
                else if (TryReadGuid(document.RootElement, out var id))
                {
                    MarkerHovered?.Invoke(this, id);
                }
            }
            else if (type is "mapClick")
            {
                if (TryReadLatLng(document.RootElement, out var lat, out var lng))
                {
                    MapClicked?.Invoke(this, (lat, lng));
                }
            }
            else if (type is "editableDragEnd")
            {
                if (TryReadLatLng(document.RootElement, out var lat, out var lng))
                {
                    EditableMarkerDragEnded?.Invoke(this, (lat, lng));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle Google Map web message.");
            _readyTcs?.TrySetException(ex);
        }
    }

    private void LogMapDiagnostics(JsonElement root, string eventName)
    {
        var width = root.TryGetProperty("width", out var w) && w.TryGetDouble(out var wd) ? wd : -1;
        var height = root.TryGetProperty("height", out var h) && h.TryGetDouble(out var ht) ? ht : -1;
        var mapTypeId = root.TryGetProperty("mapTypeId", out var mt) ? mt.GetString() : null;
        var zoom = root.TryGetProperty("zoom", out var z) && z.TryGetDouble(out var zm) ? zm : double.NaN;
        _logger.LogInformation(
            "Google Map {Event}. Size={Width}x{Height} MapTypeId={MapTypeId} Zoom={Zoom}",
            eventName,
            width,
            height,
            mapTypeId,
            zoom);
    }

    private static string TruncateUri(string uri) =>
        uri.Length <= 180 ? uri : uri[..180] + "…";

    private static bool TryReadGuid(JsonElement root, out Guid id)
    {
        id = Guid.Empty;
        if (!root.TryGetProperty("id", out var idElement))
        {
            return false;
        }

        var text = idElement.GetString();
        return Guid.TryParse(text, out id);
    }

    private static bool TryReadLatLng(JsonElement root, out double lat, out double lng)
    {
        lat = 0;
        lng = 0;
        return root.TryGetProperty("lat", out var latElement) &&
               root.TryGetProperty("lng", out var lngElement) &&
               latElement.TryGetDouble(out lat) &&
               lngElement.TryGetDouble(out lng);
    }
}

internal static class DispatcherQueueExtensions
{
    public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            tcs.SetException(new InvalidOperationException("Failed to enqueue map work on the UI thread."));
        }

        return tcs.Task;
    }

    public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Func<Task> action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
        {
            tcs.SetException(new InvalidOperationException("Failed to enqueue map work on the UI thread."));
        }

        return tcs.Task;
    }
}
