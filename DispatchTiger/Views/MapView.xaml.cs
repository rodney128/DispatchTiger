using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DispatchTiger.Models;
using DispatchTiger.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace DispatchTiger.Views
{
    public partial class MapView : UserControl
    {
        private MainViewModel? _vm;
        private bool _webViewReady;

        public MapView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Called by MainWindow after setting DataContext so we can wire ViewModel events.
        /// </summary>
        public void SetViewModel(MainViewModel vm)
        {
            if (_vm != null)
                _vm.PropertyChanged -= Vm_PropertyChanged;

            _vm = vm;
            _vm.PropertyChanged += Vm_PropertyChanged;
        }

        // ── Zone coordinate lookup ────────────────────────────────────────────────

        private static readonly Dictionary<string, (double Lat, double Lng)> ZoneCoords = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Sidney",    (48.6506, -123.3986) },
            { "Victoria",  (48.4284, -123.3656) },
            { "Langford",  (48.4496, -123.5043) },
            { "Nanaimo",   (49.1659, -123.9401) },
            { "Duncan",    (48.7787, -123.7079) },
            { "Saanich",   (48.4840, -123.3810) },
            { "Colwood",   (48.4236, -123.4958) },
            { "Oak Bay",   (48.4266, -123.3220) },
            { "Esquimalt", (48.4297, -123.4147) },
            { "Sooke",     (48.3740, -123.7276) },
        };

        /// <summary>
        /// Returns approximate lat/lng for a zone name or address string.
        /// Checks for zone-name substrings in the same order as EstimateTravelMinutes.
        /// Returns null when no recognised zone is found.
        /// </summary>
        private static (double Lat, double Lng)? TryGetZoneCoordinate(string? zoneOrAddress)
        {
            if (string.IsNullOrWhiteSpace(zoneOrAddress))
                return null;

            foreach (var kv in ZoneCoords)
                if (zoneOrAddress.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;

            return null;
        }

        // ── Event handlers ────────────────────────────────────────────────────────

        private async void MapView_Loaded(object sender, RoutedEventArgs e)
        {
            // Check API key first; show overlay and skip WebView2 init if missing
            var apiKey = Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MissingKeyOverlay.Visibility = Visibility.Visible;
                MapWebView.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                await MapWebView.EnsureCoreWebView2Async();
                _webViewReady = true;
                RefreshMap();
            }
            catch (Exception ex)
            {
                MapStatusText.Text = $"WebView2 init failed: {ex.Message}";
            }
        }

        private void RefreshMapButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshMap();
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainViewModel.SelectedJob) or nameof(MainViewModel.SelectedTruck))
            {
                // Must dispatch to UI thread since PropertyChanged can fire from any context
                Dispatcher.InvokeAsync(RefreshMap);
            }
        }

        // ── Map refresh ───────────────────────────────────────────────────────────

        private void RefreshMap()
        {
            if (!_webViewReady || _vm == null)
                return;

            var apiKey = Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY") ?? "";
            var html = BuildMapHtml(apiKey, _vm);
            MapWebView.NavigateToString(html);

            var job = _vm.SelectedJob;
            MapStatusText.Text = job != null
                ? $"Showing {_vm.AvailableTrucks.Count} trucks  ·  Job: {job.Description}"
                : $"Showing {_vm.AvailableTrucks.Count} trucks  ·  Select a job to see pickup/delivery";
        }

        // ── HTML builder ──────────────────────────────────────────────────────────

        private static string BuildMapHtml(string apiKey, MainViewModel vm)
        {
            var sb = new StringBuilder();

            // Optional Map ID for AdvancedMarkerElement (requires a real Cloud Console Map ID).
            // Read from env var; leave empty to use legacy Marker instead.
            var mapId = Environment.GetEnvironmentVariable("GOOGLE_MAPS_MAP_ID") ?? "";
            bool useAdvancedMarker = !string.IsNullOrWhiteSpace(mapId);

            // ── markers data ──────────────────────────────────────────────────────
            var markersSb = new StringBuilder();
            markersSb.Append("const markers = [];\n");

            // Truck markers
            foreach (var truck in vm.AvailableTrucks)
            {
                var coord = TryGetZoneCoordinate(truck.CurrentLocation);
                if (coord == null) continue;

                var label = EscapeJs(BuildTruckLabel(truck));
                var color = truck.IsAvailable ? "#5B9BD5" : "#888888";
                markersSb.Append(
                    $"markers.push({{ lat: {coord.Value.Lat}, lng: {coord.Value.Lng}, " +
                    $"label: '{label}', color: '{color}', type: 'truck' }});\n");
            }

            // Pickup marker
            var job = vm.SelectedJob;
            if (job != null)
            {
                var pickupCoord = TryGetZoneCoordinate(job.PickupLocation?.Zone)
                               ?? TryGetZoneCoordinate(job.PickupLocation?.Address)
                               ?? TryGetZoneCoordinate(job.PickupAddress);
                if (pickupCoord != null)
                {
                    var label = EscapeJs(BuildPickupLabel(job));
                    markersSb.Append(
                        $"markers.push({{ lat: {pickupCoord.Value.Lat}, lng: {pickupCoord.Value.Lng}, " +
                        $"label: '{label}', color: '#34A853', type: 'pickup' }});\n");
                }

                var deliveryCoord = TryGetZoneCoordinate(job.DeliveryLocation?.Zone)
                                 ?? TryGetZoneCoordinate(job.DeliveryLocation?.Address)
                                 ?? TryGetZoneCoordinate(job.DeliveryAddress);
                if (deliveryCoord != null)
                {
                    var label = EscapeJs(BuildDeliveryLabel(job));
                    markersSb.Append(
                        $"markers.push({{ lat: {deliveryCoord.Value.Lat}, lng: {deliveryCoord.Value.Lng}, " +
                        $"label: '{label}', color: '#EA4335', type: 'delivery' }});\n");
                }
            }

            // ── marker JS: AdvancedMarkerElement when Map ID present, legacy Marker otherwise ──
            string markerCreationJs = useAdvancedMarker
                ? BuildAdvancedMarkerJs()
                : BuildLegacyMarkerJs();

            // ── map options ───────────────────────────────────────────────────────
            string mapIdLine = useAdvancedMarker ? $"    mapId: '{EscapeJs(mapId)}'," : "";

            // ── libraries param ───────────────────────────────────────────────────
            string librariesParam = useAdvancedMarker ? "&libraries=marker" : "";

            // ── HTML ──────────────────────────────────────────────────────────────
            sb.Append($@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<style>
  html, body, #map {{ height: 100%; margin: 0; padding: 0; background: #1a1a1a; }}
</style>
</head>
<body>
<div id='map'></div>
<script>
{markersSb}

async function initMap() {{
  const {{ Map }} = await google.maps.importLibrary('maps');

  const map = new Map(document.getElementById('map'), {{
    center: {{ lat: 48.60, lng: -123.55 }},
    zoom: 9,
{mapIdLine}
    mapTypeId: 'roadmap',
    backgroundColor: '#1a1a1a',
  }});

  if (markers.length === 0) return;

  const bounds = new google.maps.LatLngBounds();
  markers.forEach(function(m) {{ bounds.extend({{ lat: m.lat, lng: m.lng }}); }});

{markerCreationJs}

  if (markers.length === 1) {{
    map.setCenter({{ lat: markers[0].lat, lng: markers[0].lng }});
    map.setZoom(12);
  }} else {{
    map.fitBounds(bounds, 60);
  }}
}}
</script>
<script src='https://maps.googleapis.com/maps/api/js?key={apiKey}&v=weekly{librariesParam}&callback=initMap' async defer></script>
</body>
</html>");

            return sb.ToString();
        }

        /// <summary>
        /// Marker creation JS using AdvancedMarkerElement (requires a valid Map ID).
        /// </summary>
        private static string BuildAdvancedMarkerJs() => @"
  const { AdvancedMarkerElement } = await google.maps.importLibrary('marker');

  markers.forEach(function(m) {
    const pin = document.createElement('div');
    pin.style.cssText = [
      'background:' + m.color,
      'color:#fff',
      'border-radius:6px',
      'padding:4px 8px',
      'font:bold 11px/1.3 sans-serif',
      'max-width:180px',
      'white-space:pre-wrap',
      'word-break:break-word',
      'box-shadow:0 2px 6px rgba(0,0,0,.5)',
      'cursor:pointer',
    ].join(';');
    pin.textContent = m.label;

    const marker = new AdvancedMarkerElement({
      map: map,
      position: { lat: m.lat, lng: m.lng },
      content: pin,
      title: m.label,
    });

    const info = new google.maps.InfoWindow({
      content: '<div style=""font:13px sans-serif;white-space:pre-wrap;max-width:260px"">' + m.label.replace(/\n/g,'<br>') + '</div>'
    });
    marker.addListener('click', function() { info.open({ anchor: marker, map }); });
  });
";

        /// <summary>
        /// Marker creation JS using the legacy google.maps.Marker (no Map ID required).
        /// Custom coloured label overlays are added via OverlayView so the colour is visible.
        /// </summary>
        private static string BuildLegacyMarkerJs() => @"
  markers.forEach(function(m) {
    // Pin icon colour via SVG path
    const svgIcon = {
      path: google.maps.SymbolPath.CIRCLE,
      scale: 10,
      fillColor: m.color,
      fillOpacity: 1,
      strokeColor: '#ffffff',
      strokeWeight: 2,
    };

    const marker = new google.maps.Marker({
      map: map,
      position: { lat: m.lat, lng: m.lng },
      icon: svgIcon,
      title: m.label,
    });

    const infoContent = '<div style=""font:13px sans-serif;white-space:pre-wrap;max-width:260px;padding:4px"">'
                      + m.label.replace(/\n/g,'<br>') + '</div>';
    const info = new google.maps.InfoWindow({ content: infoContent });
    marker.addListener('click', function() { info.open(map, marker); });
  });
";

        // ── Label builders ────────────────────────────────────────────────────────

        private static string BuildTruckLabel(Truck truck)
        {
            var sb = new StringBuilder();
            sb.Append("🚛 ").Append(truck.PlateNumber);
            if (!string.IsNullOrWhiteSpace(truck.VehicleType))
                sb.Append('\n').Append(truck.VehicleType);
            if (truck.Driver != null)
                sb.Append('\n').Append(truck.Driver.Name);
            sb.Append('\n').Append(truck.IsAvailable ? "Available" : "Unavailable");
            if (truck.AvailableAt.HasValue)
                sb.Append(" from ").Append(truck.AvailableAt.Value.ToString("h:mm tt"));
            return sb.ToString();
        }

        private static string BuildPickupLabel(Job job)
        {
            var sb = new StringBuilder("📦 Pickup");
            if (job.Shipper != null)
                sb.Append('\n').Append(job.Shipper.Name);
            if (job.PickupLocation != null)
                sb.Append('\n').Append(job.PickupLocation.Name);
            else if (!string.IsNullOrWhiteSpace(job.PickupAddress))
                sb.Append('\n').Append(job.PickupAddress);
            return sb.ToString();
        }

        private static string BuildDeliveryLabel(Job job)
        {
            var sb = new StringBuilder("🏁 Delivery");
            if (job.Receiver != null)
                sb.Append('\n').Append(job.Receiver.Name);
            if (job.DeliveryLocation != null)
                sb.Append('\n').Append(job.DeliveryLocation.Name);
            else if (!string.IsNullOrWhiteSpace(job.DeliveryAddress))
                sb.Append('\n').Append(job.DeliveryAddress);
            return sb.ToString();
        }

        // ── JS string escape ──────────────────────────────────────────────────────

        private static string EscapeJs(string text)
            => text
               .Replace("\\", "\\\\")
               .Replace("'", "\\'")
               .Replace("\r", "")
               .Replace("\n", "\\n");
    }
}
