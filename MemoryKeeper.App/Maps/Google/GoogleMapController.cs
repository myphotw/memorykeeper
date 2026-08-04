using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    private bool _initialized;
    private bool _ready;
    private TaskCompletionSource<bool>? _readyTcs;
    private IReadOnlyList<Guid> _matchedIds = [];

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

    public event EventHandler? Ready;

    public event EventHandler<Guid>? MarkerClicked;

    public event EventHandler<Guid?>? MarkerHovered;

    public event EventHandler<(double Lat, double Lng)>? MapClicked;

    public event EventHandler<(double Lat, double Lng)>? EditableMarkerDragEnded;

    public async Task InitializeAsync(string? apiKey, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_hostFolder);
        var htmlPath = Path.Combine(_hostFolder, "index.html");
        // Always rewrite so marker clear/filter fixes are not stuck behind a stale file.
        await File.WriteAllTextAsync(
            htmlPath,
            GoogleMapHtmlBuilder.Build(apiKey),
            cancellationToken);

        if (_initialized)
        {
            return;
        }

        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.memorykeeper.local",
            _hostFolder,
            CoreWebView2HostResourceAccessKind.Allow);

        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Cache-bust: WebView2 may otherwise keep an old index.html where clearMarkers is broken.
        var cacheBust = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _webView.Source = new Uri($"https://app.memorykeeper.local/index.html?v={cacheBust}");

        using var registration = cancellationToken.Register(() => _readyTcs.TrySetCanceled(cancellationToken));
        try
        {
            await _readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(25), cancellationToken);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Google Map initialization timed out.");
            throw new InvalidOperationException(
                "Google Maps 초기화 시간이 초과되었습니다. API Key와 네트워크를 확인하세요.",
                ex);
        }

        _initialized = true;
    }

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
            zoom = center ? 16 : (int?)null
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

    public ValueTask DisposeAsync()
    {
        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }

        return ValueTask.CompletedTask;
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
            if (type is "ready")
            {
                _ready = true;
                _readyTcs?.TrySetResult(true);
                Ready?.Invoke(this, EventArgs.Empty);
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
}
