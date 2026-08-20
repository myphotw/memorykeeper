using System.Net;
using MemoryKeeper.Application;

namespace MemoryKeeper.App.Maps.Google;

public static class GoogleMapHtmlBuilder
{
    public static string Build(string? apiKey)
    {
        var normalized = GoogleMapsApiKeyValidator.NormalizeOrNull(apiKey);
        if (normalized is null)
        {
            return OpenStreetMapHtmlBuilder.Build();
        }

        var hasKey = normalized is not null;
        // URL query value must be percent-encoded (HtmlEncode alone breaks spaces/unicode).
        var urlKey = Uri.EscapeDataString(normalized ?? string.Empty);
        var displayKeyHint = hasKey
            ? string.Empty
            : WebUtility.HtmlEncode("지도 서비스를 사용할 수 없습니다. 잠시 후 다시 시도해 주세요.");

        return $$"""
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <style>
    html, body, #map { margin: 0; padding: 0; width: 100%; height: 100%; overflow: hidden; background: #e8eaed; }
    #overlay {
      display: none;
      position: absolute;
      inset: 0;
      align-items: center;
      justify-content: center;
      background: #f3f3f3;
      color: #333;
      font-family: Segoe UI, sans-serif;
      font-size: 14px;
      padding: 24px;
      text-align: center;
      box-sizing: border-box;
      z-index: 2;
    }
  </style>
</head>
<body>
  <div id="map"></div>
  <div id="overlay"></div>
  <script>
    const hasKey = {{(hasKey ? "true" : "false")}};
    let map = null;
    let markers = [];
    let markerById = {};
    let clusterer = null;
    let selectedId = null;
    let matchedIds = {};
    let hoverId = null;
    let editableMarker = null;
    let radiusCircle = null;
    let mapClickEnabled = false;
    let clustererLoading = false;
    let clustererReady = false;
    let lastCenter = { lat: 36.5, lng: 127.9 };
    let lastZoom = 7;
    let tilesLoaded = false;

    const MAP_STYLES = [
      { featureType: 'poi', stylers: [{ visibility: 'off' }] },
      { featureType: 'poi.business', stylers: [{ visibility: 'off' }] },
      { featureType: 'poi.attraction', stylers: [{ visibility: 'off' }] },
      { featureType: 'poi.place_of_worship', stylers: [{ visibility: 'off' }] },
      { featureType: 'poi.school', stylers: [{ visibility: 'off' }] },
      { featureType: 'poi.sports_complex', stylers: [{ visibility: 'off' }] },
      { featureType: 'transit', stylers: [{ visibility: 'off' }] },
      { featureType: 'transit.station', stylers: [{ visibility: 'off' }] },
      { elementType: 'labels.icon', stylers: [{ visibility: 'off' }] }
    ];

    function post(message) {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(message);
      }
    }

    function showOverlay(text) {
      const overlay = document.getElementById('overlay');
      overlay.style.display = 'flex';
      overlay.textContent = text;
    }

    function captureConsole() {
      ['error', 'warn'].forEach(level => {
        const original = console[level].bind(console);
        console[level] = function () {
          try {
            const message = Array.prototype.slice.call(arguments).map(String).join(' ');
            post({ type: 'console', level: level, message: message });
            if (/InvalidKeyMapError|RefererNotAllowedMapError|BillingNotEnabledMapError|ApiNotActivatedMapError|ExpiredKeyMapError/i.test(message)) {
              showOverlay('Google Maps 오류: ' + message);
              post({ type: 'error', message: message });
            }
          } catch (e) {}
          return original.apply(console, arguments);
        };
      });
      window.addEventListener('error', function (event) {
        const message = (event && event.message) ? String(event.message) : 'window.error';
        post({ type: 'console', level: 'error', message: message });
      });
    }

    function ensureClusterer(onReady) {
      if (clustererReady || (window.markerClusterer && window.markerClusterer.MarkerClusterer)) {
        clustererReady = true;
        onReady();
        return;
      }
      if (clustererLoading) {
        const timer = setInterval(() => {
          if (clustererReady || (window.markerClusterer && window.markerClusterer.MarkerClusterer)) {
            clearInterval(timer);
            clustererReady = true;
            onReady();
          }
        }, 100);
        setTimeout(() => { clearInterval(timer); onReady(); }, 3000);
        return;
      }
      clustererLoading = true;
      const script = document.createElement('script');
      script.src = 'https://unpkg.com/@googlemaps/markerclusterer/dist/index.min.js';
      script.async = true;
      script.onload = function () {
        clustererReady = true;
        clustererLoading = false;
        onReady();
      };
      script.onerror = function () {
        clustererLoading = false;
        onReady();
      };
      document.head.appendChild(script);
    }

    function colorFor(item) {
      if (item.id === selectedId || item.state === 'selected') return '#C62828';
      if (matchedIds[item.id] || item.isMatched || item.state === 'matched') return '#1565C0';
      return '#78909C';
    }

    function sizeFor(item) {
      const base = Math.max(0.55, Math.min(1.7, Number(item.scale) || 1));
      const selectedBoost = (item.id === selectedId) ? 1.5 : 1;
      const hoverBoost = (item.id === hoverId) ? 1.15 : 1;
      return base * selectedBoost * hoverBoost;
    }

    function buildIcon(item) {
      const scale = sizeFor(item);
      const fill = colorFor(item);
      const stroke = (item.id === selectedId) ? '#FFFFFF' : '#ECEFF1';
      const star = item.isFavorite
        ? `<text x='16' y='20' text-anchor='middle' font-size='12' fill='#FFD54F'>★</text>`
        : '';
      const svg = `
        <svg xmlns='http://www.w3.org/2000/svg' width='32' height='42' viewBox='0 0 32 42'>
          <path d='M16 1C8.3 1 2 7.3 2 15c0 10.5 14 25 14 25s14-14.5 14-25C30 7.3 23.7 1 16 1z'
                fill='${fill}' stroke='${stroke}' stroke-width='2'/>
          <circle cx='16' cy='15' r='5.5' fill='#FFFFFF' opacity='0.95'/>
          ${star}
        </svg>`;
      return {
        url: 'data:image/svg+xml;charset=UTF-8,' + encodeURIComponent(svg),
        scaledSize: new google.maps.Size(32 * scale, 42 * scale),
        anchor: new google.maps.Point(16 * scale, 42 * scale)
      };
    }

    function refreshMarkerIcons() {
      markers.forEach(marker => {
        const item = marker.__data;
        if (!item) return;
        marker.setIcon(buildIcon(item));
        marker.setZIndex(
          item.id === selectedId ? 1000 :
          item.id === hoverId ? 900 :
          (matchedIds[item.id] ? 500 : 100)
        );
      });
    }

    function clearMarkers() {
      // Snapshot first — MarkerClusterer can leave orphans if we clear the array too early.
      const previousMarkers = markers.slice();
      const previousClusterer = clusterer;
      clusterer = null;
      markers = [];
      markerById = {};
      matchedIds = {};
      selectedId = null;
      hoverId = null;

      if (previousClusterer) {
        try { previousClusterer.clearMarkers(); } catch (e) {}
        try { previousClusterer.setMap(null); } catch (e) {}
      }

      previousMarkers.forEach(marker => {
        try {
          google.maps.event.clearInstanceListeners(marker);
          marker.setMap(null);
          marker.setVisible(false);
        } catch (e) {}
      });
    }

    function setMarkers(message) {
      // Always wipe previous markers before creating a new set (year filter must not leave leftovers).
      clearMarkers();
      (message.matchedIds || []).forEach(id => { matchedIds[id] = true; });

      (message.markers || []).forEach(item => {
        const marker = new google.maps.Marker({
          position: { lat: item.lat, lng: item.lng },
          title: item.title || '',
          icon: buildIcon(item),
          map: map,
          optimized: false
        });
        marker.__data = item;
        marker.addListener('click', () => {
          selectedId = item.id;
          refreshMarkerIcons();
          post({ type: 'markerClick', id: item.id });
        });
        marker.addListener('mouseover', () => {
          hoverId = item.id;
          refreshMarkerIcons();
          post({ type: 'markerHover', id: item.id });
        });
        marker.addListener('mouseout', () => {
          if (hoverId === item.id) {
            hoverId = null;
            refreshMarkerIcons();
            post({ type: 'markerHover', id: null });
          }
        });
        markers.push(marker);
        markerById[item.id] = marker;
      });

      refreshMarkerIcons();
    }

    function selectMarker(message) {
      selectedId = message.id || null;
      refreshMarkerIcons();
      if (!selectedId || !markerById[selectedId]) {
        return;
      }
      const marker = markerById[selectedId];
      const pos = marker.getPosition();
      if (message.center !== false && pos) {
        const currentZoom = map.getZoom();
        const requested = typeof message.zoom === 'number' ? message.zoom : 15;
        const targetZoom = normalizeZoom(requested, normalizeZoom(currentZoom, 15));
        centerOn(pos.lat(), pos.lng(), targetZoom);
      }
    }

    function highlightMarkers(message) {
      matchedIds = {};
      (message.matchedIds || []).forEach(id => { matchedIds[id] = true; });
      refreshMarkerIcons();
    }

    function clearEditablePin() {
      if (editableMarker) {
        editableMarker.setMap(null);
        editableMarker = null;
      }
      if (radiusCircle) {
        radiusCircle.setMap(null);
        radiusCircle = null;
      }
    }

    function setEditablePin(message) {
      const lat = Number(message.lat);
      const lng = Number(message.lng);
      const radiusMeters = Number(message.radiusMeters) || 0;
      const zoom = typeof message.zoom === 'number' ? message.zoom : 17;
      const position = { lat, lng };

      if (!editableMarker) {
        editableMarker = new google.maps.Marker({
          map,
          position,
          draggable: true,
          zIndex: 2000
        });
        editableMarker.addListener('dragend', () => {
          const pos = editableMarker.getPosition();
          if (!pos) return;
          if (radiusCircle) {
            radiusCircle.setCenter(pos);
          }
          post({ type: 'editableDragEnd', lat: pos.lat(), lng: pos.lng() });
        });
      } else {
        editableMarker.setPosition(position);
        editableMarker.setMap(map);
      }

      if (!radiusCircle) {
        radiusCircle = new google.maps.Circle({
          map,
          center: position,
          radius: radiusMeters,
          strokeColor: '#1565C0',
          strokeOpacity: 0.9,
          strokeWeight: 2,
          fillColor: '#1565C0',
          fillOpacity: 0.15,
          clickable: false
        });
      } else {
        radiusCircle.setCenter(position);
        radiusCircle.setRadius(radiusMeters);
        radiusCircle.setMap(map);
      }

      map.panTo(position);
      map.setZoom(zoom);
      lastCenter = position;
      lastZoom = zoom;
    }

    function updateEditableRadius(message) {
      if (!radiusCircle) return;
      radiusCircle.setRadius(Number(message.radiusMeters) || 0);
    }

    function forceRelayout() {
      if (!map) return;
      const mapDiv = map.getDiv();
      const width = mapDiv ? mapDiv.offsetWidth : 0;
      const height = mapDiv ? mapDiv.offsetHeight : 0;
      // Never resize while the host is collapsing (photo panel open) — that clears tiles to blue.
      if (width < 32 || height < 32) {
        post({
          type: 'layout',
          width: width,
          height: height,
          skipped: true,
          reason: 'host-too-small',
          tilesLoaded: tilesLoaded
        });
        return;
      }
      try {
        google.maps.event.trigger(map, 'resize');
      } catch (e) {}
      const center = map.getCenter();
      const zoom = map.getZoom();
      if (center) {
        map.setCenter(center);
        lastCenter = { lat: center.lat(), lng: center.lng() };
      } else if (lastCenter) {
        map.setCenter(lastCenter);
      }
      if (typeof zoom === 'number') {
        map.setZoom(zoom);
        lastZoom = zoom;
      } else if (typeof lastZoom === 'number') {
        map.setZoom(lastZoom);
      }
      post({
        type: 'layout',
        width: width,
        height: height,
        mapTypeId: map.getMapTypeId ? map.getMapTypeId() : null,
        zoom: map.getZoom ? map.getZoom() : null,
        tilesLoaded: tilesLoaded
      });
    }

    function normalizeZoom(zoom, fallback) {
      const value = typeof zoom === 'number' ? zoom : Number(zoom);
      if (!Number.isFinite(value) || value < 1 || value > 21) {
        return fallback;
      }
      return value;
    }

    function centerOn(lat, lng, zoom) {
      if (!map) return;
      const latitude = Number(lat);
      const longitude = Number(lng);
      if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) return;
      if (latitude === 0 && longitude === 0) return;
      if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180) return;

      const target = { lat: latitude, lng: longitude };
      lastCenter = target;
      map.panTo(target);
      const nextZoom = normalizeZoom(zoom, null);
      if (nextZoom !== null) {
        lastZoom = nextZoom;
        map.setZoom(nextZoom);
      }
      // Do NOT forceRelayout here — selection must not resize during photo-panel layout thrash.
    }

    function handleMessage(message) {
      if (!map || !message || !message.type) {
        return;
      }

      if (message.type === 'setMarkers') {
        setMarkers(message);
        return;
      }
      if (message.type === 'selectMarker') {
        selectMarker(message);
        return;
      }
      if (message.type === 'hoverMarker') {
        hoverId = message.id || null;
        refreshMarkerIcons();
        return;
      }
      if (message.type === 'highlightMarkers') {
        highlightMarkers(message);
        return;
      }
      if (message.type === 'enableMapClick') {
        mapClickEnabled = !!message.enabled;
        return;
      }
      if (message.type === 'setEditablePin') {
        setEditablePin(message);
        return;
      }
      if (message.type === 'updateEditableRadius') {
        updateEditableRadius(message);
        return;
      }
      if (message.type === 'clearEditablePin') {
        clearEditablePin();
        return;
      }
      if (message.type === 'resize') {
        forceRelayout();
        return;
      }
      if (message.type === 'center') {
        centerOn(message.lat, message.lng, message.zoom);
        return;
      }
      if (message.type === 'setZoom') {
        lastZoom = message.zoom;
        map.setZoom(message.zoom);
        return;
      }
      if (message.type === 'zoomBy') {
        lastZoom = (map.getZoom() || 6) + (message.delta || 0);
        map.setZoom(lastZoom);
        return;
      }
      if (message.type === 'fitMarkers') {
        if (markers.length === 0) return;
        if (markers.length === 1) {
          const pos = markers[0].getPosition();
          if (pos) centerOn(pos.lat(), pos.lng(), 16);
          return;
        }
        const bounds = new google.maps.LatLngBounds();
        markers.forEach(marker => bounds.extend(marker.getPosition()));
        const mapDiv = map.getDiv();
        const width = mapDiv && mapDiv.offsetWidth ? mapDiv.offsetWidth : 640;
        const height = mapDiv && mapDiv.offsetHeight ? mapDiv.offsetHeight : 480;
        if (width < 32 || height < 32) {
          return;
        }
        // MK-053: ~12% padding on each side so edge markers never touch the frame border.
        map.fitBounds(bounds, {
          top: Math.round(height * 0.12),
          bottom: Math.round(height * 0.12),
          left: Math.round(width * 0.12),
          right: Math.round(width * 0.12)
        });
        const c = map.getCenter();
        if (c) {
          lastCenter = { lat: c.lat(), lng: c.lng() };
        }
        lastZoom = map.getZoom() || lastZoom;
      }
    }

    function initMap() {
      tilesLoaded = false;
      // Must only run from Maps JS callback — never create Map before google.maps is ready.
      if (!window.google || !google.maps || !google.maps.Map) {
        showOverlay('Google Maps JS API가 아직 준비되지 않았습니다.');
        post({ type: 'error', message: 'google.maps.Map is not available in callback.' });
        return;
      }

      const mapDiv = document.getElementById('map');
      const options = {
        center: lastCenter,
        zoom: lastZoom,
        mapTypeId: google.maps.MapTypeId.ROADMAP,
        mapTypeControl: false,
        streetViewControl: false,
        fullscreenControl: false,
        clickableIcons: false,
        styles: MAP_STYLES,
        // WebView2 often fails vector/WebGL tiles (solid blue) while DOM markers still render.
        // Prefer classic raster tiles when the API exposes RenderingType.
        ...(google.maps.RenderingType
          ? { renderingType: google.maps.RenderingType.RASTER }
          : {})
      };

      map = new google.maps.Map(mapDiv, options);

      map.addListener('click', event => {
        if (!mapClickEnabled || !event.latLng) return;
        post({ type: 'mapClick', lat: event.latLng.lat(), lng: event.latLng.lng() });
      });

      map.addListener('tilesloaded', function () {
        tilesLoaded = true;
        post({
          type: 'tilesLoaded',
          width: mapDiv.offsetWidth,
          height: mapDiv.offsetHeight,
          mapTypeId: map.getMapTypeId(),
          zoom: map.getZoom()
        });
      });

      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', event => handleMessage(event.data));
      }

      window.addEventListener('resize', function () {
        forceRelayout();
      });

      // Ready after Map construction (callback path guarantees API load).
      post({
        type: 'mapReady',
        width: mapDiv.offsetWidth,
        height: mapDiv.offsetHeight,
        mapTypeId: map.getMapTypeId(),
        zoom: map.getZoom(),
        renderingType: google.maps.RenderingType ? 'RASTER' : 'DEFAULT'
      });
      // Backward-compatible alias for older hosts.
      post({
        type: 'ready',
        width: mapDiv.offsetWidth,
        height: mapDiv.offsetHeight,
        mapTypeId: map.getMapTypeId(),
        zoom: map.getZoom(),
        renderingType: google.maps.RenderingType ? 'RASTER' : 'DEFAULT'
      });

      // Div may still be 0x0 on first paint inside WinUI WebView2 — resize after layout/idle.
      google.maps.event.addListenerOnce(map, 'idle', function () {
        forceRelayout();
      });
      setTimeout(forceRelayout, 50);
      setTimeout(forceRelayout, 250);
      setTimeout(function () {
        forceRelayout();
        if (!tilesLoaded) {
          post({
            type: 'console',
            level: 'warn',
            message: 'Map tiles not loaded after 4s. Check API key referrer (https://app.memorykeeper.local/*), Maps JavaScript API, billing, or WebGL.'
          });
        }
      }, 4000);
    }

    captureConsole();
    window.initMap = initMap;
    window.gm_authFailure = function () {
      showOverlay('지도 서비스를 사용할 수 없습니다. 잠시 후 다시 시도해 주세요.');
      post({ type: 'error', message: 'Google Maps authentication failed (gm_authFailure).' });
    };

    if (!hasKey) {
      showOverlay('{{displayKeyHint}}');
      post({ type: 'error', message: 'Google Maps API key is not configured.' });
    } else {
      const script = document.createElement('script');
      script.src = 'https://maps.googleapis.com/maps/api/js?key={{urlKey}}&callback=initMap&v=weekly&loading=async';
      script.async = true;
      script.defer = true;
      script.onerror = function () {
        showOverlay('지도를 불러오지 못했습니다. 네트워크 연결을 확인해 주세요.');
        post({ type: 'error', message: 'Failed to load Google Maps script.' });
      };
      document.head.appendChild(script);
      setTimeout(function () {
        if (!map) {
          showOverlay('지도를 불러오는 데 시간이 오래 걸리고 있습니다. 잠시 후 다시 시도해 주세요.');
          post({ type: 'error', message: 'Google Maps initialization timed out.' });
        }
      }, 20000);
    }
  </script>
</body>
</html>
""";
    }
}
