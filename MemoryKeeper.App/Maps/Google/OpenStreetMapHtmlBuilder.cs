namespace MemoryKeeper.App.Maps.Google;

/// <summary>
/// Keyless display fallback. Geocoding remains exclusively on TC-Backend;
/// this document only renders coordinates using OpenStreetMap tiles.
/// </summary>
internal static class OpenStreetMapHtmlBuilder
{
    public static string Build() => """
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
  <style>
    html, body, #map { margin: 0; width: 100%; height: 100%; overflow: hidden; background: #e8eaed; }
    .leaflet-container { font-family: "Segoe UI", sans-serif; }
    .mk-pin { border: 2px solid #fff; box-shadow: 0 1px 5px rgba(0,0,0,.35); }
  </style>
</head>
<body>
  <div id="map"></div>
  <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
  <script>
    const post = message => {
      if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(message);
    };
    const diagnostics = type => ({
      type,
      width: document.documentElement.clientWidth,
      height: document.documentElement.clientHeight,
      mapTypeId: 'openstreetmap',
      zoom: map ? map.getZoom() : 7
    });

    let map = null;
    let layer = null;
    let markerById = {};
    let markerDataById = {};
    let selectedId = null;
    let hoverId = null;
    let matchedIds = {};
    let editableMarker = null;
    let radiusCircle = null;
    let mapClickEnabled = false;

    function colorFor(id) {
      if (id === selectedId) return '#d93025';
      if (id === hoverId) return '#f9ab00';
      if (matchedIds[id]) return '#1a73e8';
      return '#5f6368';
    }

    function refreshMarker(id) {
      const marker = markerById[id];
      const data = markerDataById[id];
      if (!marker || !data) return;
      const scale = Math.max(.75, Math.min(1.8, Number(data.scale) || 1));
      marker.setStyle({
        color: '#fff', weight: 2, fillColor: colorFor(id), fillOpacity: .95,
        radius: (id === selectedId ? 10 : 7) * scale
      });
    }

    function refreshAll() { Object.keys(markerById).forEach(refreshMarker); }

    function safePopup(data) {
      const root = document.createElement('div');
      const title = document.createElement('strong');
      title.textContent = data.title || '';
      root.appendChild(title);
      if (data.info) {
        const line = document.createElement('div');
        line.textContent = String(data.info).replace(/<br\s*\/?>/gi, ' · ');
        root.appendChild(line);
      }
      return root;
    }

    function setMarkers(items, ids) {
      layer.clearLayers();
      markerById = {};
      markerDataById = {};
      matchedIds = {};
      (ids || []).forEach(id => matchedIds[id] = true);
      (items || []).forEach(data => {
        if (!Number.isFinite(data.lat) || !Number.isFinite(data.lng)) return;
        markerDataById[data.id] = data;
        const marker = L.circleMarker([data.lat, data.lng], {
          className: 'mk-pin', radius: 7, color: '#fff', weight: 2, fillOpacity: .95
        });
        marker.on('click', () => post({ type: 'markerClick', id: data.id }));
        marker.on('mouseover', () => { hoverId = data.id; refreshAll(); post({ type: 'markerHover', id: data.id }); });
        marker.on('mouseout', () => { hoverId = null; refreshAll(); post({ type: 'markerHover', id: null }); });
        marker.bindPopup(safePopup(data));
        marker.addTo(layer);
        markerById[data.id] = marker;
      });
      refreshAll();
    }

    function fitMarkers() {
      const markers = Object.values(markerById);
      if (!markers.length) return;
      const bounds = L.latLngBounds(markers.map(marker => marker.getLatLng()));
      map.fitBounds(bounds, { padding: [40, 40], maxZoom: 15 });
    }

    function clearEditablePin() {
      if (editableMarker) map.removeLayer(editableMarker);
      if (radiusCircle) map.removeLayer(radiusCircle);
      editableMarker = null;
      radiusCircle = null;
    }

    function setEditablePin(message) {
      clearEditablePin();
      const point = [message.lat, message.lng];
      editableMarker = L.marker(point, { draggable: true }).addTo(map);
      radiusCircle = L.circle(point, { radius: Number(message.radiusMeters) || 0, color: '#1a73e8', fillOpacity: .12 }).addTo(map);
      editableMarker.on('drag', e => radiusCircle.setLatLng(e.target.getLatLng()));
      editableMarker.on('dragend', e => {
        const p = e.target.getLatLng();
        radiusCircle.setLatLng(p);
        post({ type: 'editableDragEnd', lat: p.lat, lng: p.lng });
      });
      map.setView(point, message.zoom || 17);
    }

    function handle(message) {
      switch (message.type) {
        case 'setMarkers': setMarkers(message.markers, message.matchedIds); break;
        case 'selectMarker':
          selectedId = message.id || null; refreshAll();
          if (selectedId && markerById[selectedId] && message.center !== false) {
            map.setView(markerById[selectedId].getLatLng(), message.zoom || 15);
          }
          break;
        case 'hoverMarker': hoverId = message.id || null; refreshAll(); break;
        case 'highlightMarkers': matchedIds = {}; (message.matchedIds || []).forEach(id => matchedIds[id] = true); refreshAll(); break;
        case 'center': map.setView([message.lat, message.lng], message.zoom || map.getZoom()); break;
        case 'setZoom': map.setZoom(message.zoom); break;
        case 'fitMarkers': fitMarkers(); break;
        case 'zoomBy': map.setZoom(map.getZoom() + (message.delta || 0)); break;
        case 'enableMapClick': mapClickEnabled = !!message.enabled; break;
        case 'setEditablePin': setEditablePin(message); break;
        case 'updateEditableRadius': if (radiusCircle) radiusCircle.setRadius(Number(message.radiusMeters) || 0); break;
        case 'clearEditablePin': clearEditablePin(); break;
        case 'resize': map.invalidateSize(); post(diagnostics('layout')); break;
      }
    }

    try {
      map = L.map('map', { zoomControl: true }).setView([36.5, 127.9], 7);
      layer = L.layerGroup().addTo(map);
      const tiles = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; OpenStreetMap contributors'
      });
      tiles.once('load', () => post(diagnostics('tilesLoaded')));
      tiles.addTo(map);
      map.on('click', e => {
        if (mapClickEnabled) post({ type: 'mapClick', lat: e.latlng.lat, lng: e.latlng.lng });
      });
      window.chrome.webview.addEventListener('message', event => handle(event.data));
      post(diagnostics('mapReady'));
    } catch (error) {
      post({ type: 'error', message: '지도를 표시하지 못했습니다.' });
    }
  </script>
</body>
</html>
""";
}
