using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DispatchTiger.Models;
using DispatchTiger.Services;
using DispatchTiger.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace DispatchTiger.Views
{
    public partial class MapView : UserControl
    {
        private MainViewModel? _vm;
        private bool _webViewReady;
        private bool _mapHtmlLoaded;                                    // true once NavigationCompleted fires successfully
        private int? _lastFittedJobId;                                  // id of the last job we called fitBounds for; prevents refitting the same job
        private System.Threading.CancellationTokenSource? _markerDebounce; // collapses rapid PropertyChanged firings

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


        /// <summary>
        /// Spreads markers that share the same base coordinate into a small circle so
        /// they do not visually stack. Returns the original point when count <= 1.
        /// Offset is deterministic: each index maps to a fixed angle.
        /// Radius grows slightly with group size but is capped near the zone.
        /// </summary>
        private static (double Lat, double Lng) ApplyMarkerOffset(double baseLat, double baseLng, int index, int count)
        {
            if (count <= 1) return (baseLat, baseLng);
            double radius = Math.Min(0.006 + (count - 2) * 0.001, 0.012);
            double angle = 2 * Math.PI * index / count;
            return (baseLat + Math.Cos(angle) * radius, baseLng + Math.Sin(angle) * radius);
        }
        // ── Event handlers ────────────────────────────────────────────────────────

        private async void MapView_Loaded(object sender, RoutedEventArgs e)
        {
            await TryInitializeMapAsync();
        }

        private void RefreshMapButton_Click(object sender, RoutedEventArgs e)
        {
            FullReloadMap();
        }

        private async void FitToJobButton_Click(object sender, RoutedEventArgs e)
        {
            // Manual recentre — fits to all visible markers without a full reload.
            await PushMarkersAsync(fitBounds: true);
        }

        private void AssignStagedButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            var job    = _vm.SelectedJob;
            var staged = _vm.StagedTruck;
            if (job == null || staged == null) return;
            if (job.Status != DispatchStatus.Unassigned) return;

            // Capture display values before Execute clears SelectedJob/StagedTruck
            string capturedDesc  = job.Description;
            string capturedPlate = staged.PlateNumber;

            // Mirror the DayView assignment path exactly:
            // set SelectedTruck at the moment of assignment, then invoke the command.
            _vm.SelectedTruck = staged;

            if (_vm.AssignJobCommand.CanExecute(null))
            {
                _vm.AssignJobCommand.Execute(null);
                string ts = DateTime.Now.ToString("h:mm tt");
                _vm.StatusMessage = $"\u2713 {ts} \u00B7 Assigned \"{capturedDesc}\" to {capturedPlate}";
            }

            // AssignJob() clears SelectedJob/SelectedTruck/StagedTruck.
            // Vm_PropertyChanged fires → PushMarkersAsync → buttons collapse automatically.
            // No NavigateToString, no manual marker reload needed here.
        }

        private void CancelStagingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            // Clear staging only; keep SelectedJob so the dispatcher can re-pick a truck.
            _vm.StagedTruck = null;
            // Vm_PropertyChanged fires → PushMarkersAsync handles toolbar/marker update.
        }

        // ── Map initialisation ───────────────────────────────────────────────────────────

        private async Task TryInitializeMapAsync()
        {
            var apiKey = Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY");
            UpdateSetupStatusLine();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MissingKeyOverlay.Visibility = Visibility.Visible;
                MapWebView.Visibility = Visibility.Collapsed;
                return;
            }

            MissingKeyOverlay.Visibility = Visibility.Collapsed;
            MapWebView.Visibility = Visibility.Visible;

            if (!_webViewReady)
            {
                try
                {
                    await MapWebView.EnsureCoreWebView2Async();
                    MapWebView.CoreWebView2.WebMessageReceived += HandleWebMessage;

                    // Push markers after every successful navigation (initial load + Refresh Map).
                    // _mapHtmlLoaded guards PushMarkersAsync so it only runs when the page is ready.
                    MapWebView.CoreWebView2.NavigationCompleted += async (s, ev) =>
                    {
                        if (!ev.IsSuccess) return;
                        _mapHtmlLoaded = true;
                        // Give initMap() a moment to finish constructing the map object before
                        // pushing markers.  Google Maps fires the callback asynchronously, so a
                        // short delay is enough for the typical case.
                        await Task.Delay(500);
                        await Dispatcher.InvokeAsync(async () => await PushMarkersAsync(fitBounds: true));
                    };

                    _webViewReady = true;
                }
                catch (Exception ex)
                {
                    MapStatusText.Text = $"WebView2 init failed: {ex.Message}";
                    return;
                }
            }

            FullReloadMap();
        }

        private void UpdateSetupStatusLine()
        {
            var apiKey = Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY");
            var mapId  = Environment.GetEnvironmentVariable("GOOGLE_MAPS_MAP_ID");

            SetupStatusApiKey.Text = string.IsNullOrWhiteSpace(apiKey)
                ? "GOOGLE_MAPS_API_KEY:  Missing"
                : "GOOGLE_MAPS_API_KEY:  Found";

            SetupStatusMapId.Text = string.IsNullOrWhiteSpace(mapId)
                ? "GOOGLE_MAPS_MAP_ID:   Optional — not set"
                : "GOOGLE_MAPS_MAP_ID:   Found";

            SetupStatusApiKey.Foreground = string.IsNullOrWhiteSpace(apiKey)
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 80, 80))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(126, 198, 126));

            SetupStatusMapId.Foreground = string.IsNullOrWhiteSpace(mapId)
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 102, 102))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(126, 126, 198));
        }

        // ── Setup guide button handlers ───────────────────────────────────────────

        private void CopyApiKeyCommand_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText("setx GOOGLE_MAPS_API_KEY \"PASTE_YOUR_KEY_HERE\"");
        }

        private void CopyMapIdCommand_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText("setx GOOGLE_MAPS_MAP_ID \"PASTE_MAP_ID_HERE\"");
        }

        private void OpenCloudConsole_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://console.cloud.google.com/") { UseShellExecute = true });
        }

        private void OpenMapsDocs_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://developers.google.com/maps/documentation/javascript/get-api-key") { UseShellExecute = true });
        }

        private async void SetupRefreshMap_Click(object sender, RoutedEventArgs e)
        {
            await TryInitializeMapAsync();
        }

        /// <summary>
        /// Handles messages posted from the map page via chrome.webview.postMessage.
        /// Currently handles: { type: "truckClicked", truckId: N }
        /// Sets MainViewModel.SelectedTruck and, when a job is selected, MainViewModel.StagedTruck.
        /// Does NOT execute AssignJobCommand.
        /// </summary>
        private void HandleWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (_vm == null) return;

            try
            {
                // Parse the JSON message
                var json = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
                var root = json.RootElement;

                if (!root.TryGetProperty("type", out var typeProp)) return;
                if (typeProp.GetString() != "truckClicked") return;
                if (!root.TryGetProperty("truckId", out var idProp)) return;

                int truckId = idProp.GetInt32();
                var truck = null as Truck;
                foreach (var t in _vm.AvailableTrucks)
                    if (t.Id == truckId) { truck = t; break; }

                if (truck == null) return;

                // Set SelectedTruck — does NOT assign; assignment still requires DayView Assign button
                _vm.SelectedTruck = truck;

                var job = _vm.SelectedJob;
                if (!truck.IsAvailable)
                {
                    _vm.StatusMessage = $"Selected {truck.PlateNumber} from map — truck is unavailable. Review before assigning.";
                }
                else if (job != null)
                {
                    // Stage the truck so Day View shows the Assign button immediately
                    _vm.StagedTruck = truck;
                    _vm.StatusMessage = $"Staged {truck.PlateNumber} from map for Job {job.Id}. Review fit before assigning.";
                }
                else
                {
                    // No job selected — do not stage; selection only
                    _vm.StatusMessage = $"Selected {truck.PlateNumber} from map. Select a job to stage for assignment.";
                }

                // Toolbar truck status line
                UpdateTruckStatusText();

                // SelectedTruck and StagedTruck changes both fire Vm_PropertyChanged -> PushMarkersAsync.
                // UpdateTruckStatusText is called synchronously here so the toolbar updates
                // immediately without waiting for the debounced async push.
            }
            catch
            {
                // Ignore malformed messages
            }
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(MainViewModel.SelectedJob)
                                   or nameof(MainViewModel.SelectedTruck)
                                   or nameof(MainViewModel.StagedTruck)))
                return;

            // Viewport rules:
            //   SelectedJob  → different job : fitBounds = true  (fit once to pickup/delivery/trucks)
            //   SelectedJob  → same job again : fitBounds = false (suppress re-binding noise)
            //   SelectedTruck / StagedTruck   : fitBounds = false (marker colour update only)
            bool fitBounds = false;
            if (e.PropertyName == nameof(MainViewModel.SelectedJob))
            {
                int? newJobId = _vm?.SelectedJob?.Id;
                // Only fit when the job truly changed (null→job, job→null, job→differentJob).
                if (newJobId != _lastFittedJobId)
                {
                    fitBounds = true;
                    _lastFittedJobId = newJobId;    // record now so a superseded debounce tick can't re-trigger
                }
                // Same job re-selected — keep the current center/zoom.
            }
            // SelectedTruck / StagedTruck: markers update in-place; viewport never moves.

            // Cancel any pending debounce tick, then start a fresh one.
            // This collapses rapid SelectedTruck + StagedTruck changes (from a single map click)
            // into a single marker push ~150 ms later.
            var cts = new System.Threading.CancellationTokenSource();
            System.Threading.Interlocked.Exchange(ref _markerDebounce, cts)?.Cancel();

            bool doFit = fitBounds;   // capture for the lambda
            var token  = cts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(150, token);
                    // Switch to the UI thread to call ExecuteScriptAsync + update toolbar
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        if (!token.IsCancellationRequested)
                            await PushMarkersAsync(fitBounds: doFit);
                    });
                }
                catch (System.Threading.Tasks.TaskCanceledException) { /* superseded — ignore */ }
            });
        }

        // ── Map refresh ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Full page reload: rebuilds the HTML skeleton and calls NavigateToString.
        /// Called only on initial load, API key change, or explicit Refresh Map click.
        /// Markers are pushed by NavigationCompleted -> PushMarkersAsync.
        /// </summary>
        private void FullReloadMap()
        {
            if (!_webViewReady || _vm == null)
                return;

            _mapHtmlLoaded = false;     // page is reloading; PushMarkersAsync will no-op until NavigationCompleted fires
            var apiKey = Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY") ?? "";
            MapWebView.NavigateToString(BuildMapHtml(apiKey));
        }

        private void UpdateTruckStatusText()
        {
            var staged   = _vm?.StagedTruck;
            var selected = _vm?.SelectedTruck;
            var job      = _vm?.SelectedJob;

            if (staged != null)
            {
                var status = staged.IsAvailable ? "available" : "unavailable";
                MapTruckStatusText.Text = $"Staged: {staged.PlateNumber}  \u00b7  {status}  \u00b7  Switch to Day View to assign";
                var colour = staged.IsAvailable
                    ? System.Windows.Media.Color.FromRgb(255, 215, 0)   // gold when available
                    : System.Windows.Media.Color.FromRgb(255, 140, 0);  // amber when unavailable
                MapTruckStatusText.Foreground = new System.Windows.Media.SolidColorBrush(colour);
            }
            else if (selected != null)
            {
                var status = selected.IsAvailable ? "available" : "unavailable";
                string suffix = job != null ? "  \u00b7  Select a candidate row to stage" : "  \u00b7  Select a job to stage for assignment";
                MapTruckStatusText.Text = $"Selected: {selected.PlateNumber}  \u00b7  {status}{suffix}";
                MapTruckStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(170, 170, 170));
            }
            else
            {
                MapTruckStatusText.Text = "Selected truck: none";
                MapTruckStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(170, 170, 170));
            }
        }

		/// <summary>
		/// Pushes the current marker data into the live map page via ExecuteScriptAsync.
		/// Does NOT call NavigateToString — the viewport is preserved.
		/// No-ops silently if the page is not yet ready.
		/// </summary>
		private async Task PushMarkersAsync(bool fitBounds)
		{
			if (!_mapHtmlLoaded || !_webViewReady || _vm == null)
				return;

			string json       = BuildMarkersJson(_vm);
			string routeJson  = BuildRoutePreviewJson(_vm);
			string fitArg     = fitBounds ? "true" : "false";
			// Escape both JSON strings for embedding as JS string literals
			string escaped      = json.Replace("\\", "\\\\").Replace("'", "\\'");
			string routeEscaped = routeJson == "null" ? "null" : $"'{routeJson.Replace("\\", "\\\\").Replace("'", "\\'")}'";
			string callJs       = $"window.dispatchTigerSetMarkers('{escaped}', {routeEscaped}, {fitArg});";

			try
			{
				await MapWebView.ExecuteScriptAsync(callJs);
			}
			catch
			{
				// WebView not ready or page navigating — silently ignore
			}

			// Update toolbar text (same as before, just no map reload)
				var job = _vm.SelectedJob;
				MapStatusText.Text = job != null
					? $"Showing {_vm.AvailableTrucks.Count} trucks  \u00b7  Job: {job.Description}"
					: $"Showing {_vm.AvailableTrucks.Count} trucks  \u00b7  Select a job to see pickup/delivery";

				// Assign / Cancel Staging buttons:
				// Visible only when a job is selected, a truck is staged, and the job is still unassigned.
				bool showAssign = job != null
					&& _vm.StagedTruck != null
					&& job.Status == DispatchStatus.Unassigned;

				var assignVis = showAssign ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
				AssignStagedButton.Content     = showAssign ? $"✓  Assign {_vm.StagedTruck!.PlateNumber}" : "✓  Assign";
				AssignStagedButton.Visibility  = assignVis;
				CancelStagingButton.Visibility = assignVis;

				if (job != null)
				{
					MapFitLegendText.Text = "Fit:  ★ Best  • Good  ▲ Risky  • Poor  • Blocked  — badge on each marker";
						MapFitLegendText.Visibility = System.Windows.Visibility.Visible;

						// Route legend \u2014 straight-line preview only, not driving directions
						MapRouteLegendText.Text = "Route preview (straight line):  ┄┄ truck → pickup  ── pickup → delivery";
						MapRouteLegendText.Visibility = System.Windows.Visibility.Visible;

					// Show manual recentre button so the dispatcher can fit back after panning
					FitToJobButton.Visibility = System.Windows.Visibility.Visible;

					// Recommended truck line
					var rec = PickRecommendedTruck(job, _vm.AvailableTrucks);
					if (rec != null)
					{
						var (tLbl, _) = DispatchFitService.GetTimeFit(job, rec);
						var (rLbl, _) = DispatchFitService.GetRouteFit(job, rec);
						var (eLbl, _) = DispatchFitService.GetEquipmentFit(job, rec);
						string recFit = DispatchFitService.GetOverallFit(rec, tLbl, rLbl, eLbl);
						MapRecommendedText.Text = $"⭐ Recommended: {rec.PlateNumber}  ·  {recFit} fit  ·  click marker to stage";
						MapRecommendedText.Visibility = System.Windows.Visibility.Visible;
					}
					else
					{
						MapRecommendedText.Text = "Recommended: none";
						MapRecommendedText.Visibility = System.Windows.Visibility.Visible;
					}
				}
				else
				{
					FitToJobButton.Visibility = System.Windows.Visibility.Collapsed;
						MapFitLegendText.Visibility = System.Windows.Visibility.Collapsed;
						MapRecommendedText.Visibility = System.Windows.Visibility.Collapsed;
						MapRouteLegendText.Visibility = System.Windows.Visibility.Collapsed;
				}

				UpdateTruckStatusText();
		}


        // ── HTML builder ──────────────────────────────────────────────────────────

		private static string BuildMapHtml(string apiKey)
		{
			var mapId = Environment.GetEnvironmentVariable("GOOGLE_MAPS_MAP_ID") ?? "";
			bool useAdvancedMarker = !string.IsNullOrWhiteSpace(mapId);
			string mapIdLine      = useAdvancedMarker ? $"    mapId: '{EscapeJs(mapId)}'," : "";
			string librariesParam = useAdvancedMarker ? "&libraries=marker" : "";
			string dtUseAdv       = useAdvancedMarker ? "true" : "false";

			return
$@"<!DOCTYPE html>
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
window.dispatchTigerMap        = null;
window.dispatchTigerMarkerObjs = [];
window.dispatchTigerRouteLines = [];   // Polyline objects for the current route preview
window.dispatchTigerInfoWindow = null;
var _dtUseAdvanced = {dtUseAdv};

async function initMap() {{
  const {{ Map }} = await google.maps.importLibrary('maps');
  window.dispatchTigerMap = new Map(document.getElementById('map'), {{
	center: {{ lat: 48.60, lng: -123.55 }},
	zoom: 9,
{mapIdLine}
	mapTypeId: 'roadmap',
	backgroundColor: '#1a1a1a',
  }});
  window.dispatchTigerInfoWindow = new google.maps.InfoWindow();
}}

window.dispatchTigerClearMarkers = function() {{
  window.dispatchTigerMarkerObjs.forEach(function(m) {{
	if (m.setMap) m.setMap(null);
	else if (typeof m.map !== 'undefined') m.map = null;
  }});
  window.dispatchTigerMarkerObjs = [];
}};

// Removes all current route preview polylines from the map.
window.dispatchTigerClearRouteLines = function() {{
  window.dispatchTigerRouteLines.forEach(function(l) {{ l.setMap(null); }});
  window.dispatchTigerRouteLines = [];
}};

// jsonStr    : JSON array of marker objects (unchanged from before)
// routeJson  : JSON object with optional truckToPickup / pickupToDelivery legs, or null
// fitBounds  : boolean — whether to fit the viewport to all visible points
window.dispatchTigerSetMarkers = async function(jsonStr, routeJson, fitBounds) {{
  if (!window.dispatchTigerMap) return;
  window.dispatchTigerClearMarkers();
  window.dispatchTigerClearRouteLines();
  var markers; try {{ markers = JSON.parse(jsonStr); }} catch(e) {{ return; }}
  if (!markers || markers.length === 0) return;
  var map  = window.dispatchTigerMap;
  var info = window.dispatchTigerInfoWindow;
  if (_dtUseAdvanced) {{
	var {{ AdvancedMarkerElement }} = await google.maps.importLibrary('marker');
	markers.forEach(function(m) {{
	  var pin;
	  if (m.type === 'truck' && m.icon) {{
		pin = document.createElement('img');
		pin.src = m.icon;
		pin.width  = m.iconW || 48;
		pin.height = m.iconH || 32;
		pin.style.cssText = 'cursor:pointer;display:block;filter:drop-shadow(0 2px 3px rgba(0,0,0,.6))';
	  }} else {{
		pin = document.createElement('div');
		pin.style.cssText = ['background:'+m.color,'color:#fff','border-radius:6px','padding:4px 8px','font:bold 11px/1.3 sans-serif','max-width:180px','white-space:pre-wrap','word-break:break-word','box-shadow:0 2px 6px rgba(0,0,0,.5)','cursor:pointer'].join(';');
		pin.textContent = m.label;
	  }}
	  var marker = new AdvancedMarkerElement({{ map:map, position:{{lat:m.lat,lng:m.lng}}, content:pin, title:m.label }});
	  marker.addListener('click', function() {{
		info.setContent('<div style=""font:13px sans-serif;white-space:pre-wrap;max-width:260px"">' + m.label.replace(/\n/g,'<br>') + '</div>');
		info.open({{ anchor:marker, map }});
		if (m.type==='truck' && window.chrome && chrome.webview) chrome.webview.postMessage({{type:'truckClicked',truckId:m.truckId}});
	  }});
	  window.dispatchTigerMarkerObjs.push(marker);
	}});
  }} else {{
	markers.forEach(function(m) {{
	  var iw = m.iconW || 48, ih = m.iconH || 32;
	  var icon = m.icon
		? {{ url:m.icon, scaledSize:new google.maps.Size(iw,ih), anchor:new google.maps.Point(iw/2,ih-4) }}
		: {{ path:google.maps.SymbolPath.CIRCLE, scale:10, fillColor:m.color, fillOpacity:1, strokeColor:'#ffffff', strokeWeight:2 }};
	  var marker = new google.maps.Marker({{ map:map, position:{{lat:m.lat,lng:m.lng}}, icon:icon, title:m.label }});
	  marker.addListener('click', function() {{
		info.setContent('<div style=""font:13px sans-serif;white-space:pre-wrap;max-width:260px;padding:4px"">' + m.label.replace(/\n/g,'<br>') + '</div>');
		info.open(map, marker);
		if (m.type==='truck' && window.chrome && chrome.webview) chrome.webview.postMessage({{type:'truckClicked',truckId:m.truckId}});
	  }});
	  window.dispatchTigerMarkerObjs.push(marker);
	}});
  }}

  // Draw straight-line route preview polylines (no Directions API).
  // routeJson may be null (no job selected) or contain truckToPickup and/or pickupToDelivery legs.
  var routeBoundsPoints = [];
  if (routeJson) {{
	var route; try {{ route = JSON.parse(routeJson); }} catch(e) {{ route = null; }}
	if (route) {{
	  // Dashed gold line: truck (or recommended truck) → pickup (deadhead / empty leg)
	  if (route.truckToPickup) {{
		var leg = route.truckToPickup;
		var path = [{{lat:leg.fromLat,lng:leg.fromLng}},{{lat:leg.toLat,lng:leg.toLng}}];
		var opacity = leg.isStaged ? 0.85 : 0.55;   // staged truck line looks stronger
		var weight  = leg.isStaged ? 3   : 2;
		var dashedLine = new google.maps.Polyline({{
		  map: map,
		  path: path,
		  strokeColor: '#F5A623',    // gold-orange: deadhead / empty leg
		  strokeOpacity: 0,          // transparent stroke so dashes show through
		  strokeWeight: 0,
		  icons: [{{
			icon: {{ path:'M 0,-1 0,1', strokeOpacity: opacity, strokeWeight: weight, scale: 4 }},
			offset: '0', repeat: '14px'
		  }}]
		}});
		window.dispatchTigerRouteLines.push(dashedLine);
		routeBoundsPoints.push({{lat:leg.fromLat,lng:leg.fromLng}});
		routeBoundsPoints.push({{lat:leg.toLat,  lng:leg.toLng  }});
	  }}
	  // Solid teal line: pickup → delivery (loaded leg)
	  if (route.pickupToDelivery) {{
		var leg2 = route.pickupToDelivery;
		var solidLine = new google.maps.Polyline({{
		  map: map,
		  path: [{{lat:leg2.fromLat,lng:leg2.fromLng}},{{lat:leg2.toLat,lng:leg2.toLng}}],
		  strokeColor: '#4DD0C4',    // teal: loaded / revenue leg
		  strokeOpacity: 0.80,
		  strokeWeight: 3
		}});
		window.dispatchTigerRouteLines.push(solidLine);
		routeBoundsPoints.push({{lat:leg2.fromLat,lng:leg2.fromLng}});
		routeBoundsPoints.push({{lat:leg2.toLat,  lng:leg2.toLng  }});
	  }}
	}}
  }}

  if (fitBounds) {{
	// Build bounds from markers + route endpoints so manual Fit to Job includes route.
	var allPoints = markers.map(function(m){{return {{lat:m.lat,lng:m.lng}};}})
						   .concat(routeBoundsPoints);
	if (allPoints.length === 1) {{
	  map.setCenter(allPoints[0]); map.setZoom(12);
	}} else {{
	  var b = new google.maps.LatLngBounds();
	  allPoints.forEach(function(p){{ b.extend(p); }});
	  map.fitBounds(b, 60);
	}}
  }}
}};
</script>
<script src='https://maps.googleapis.com/maps/api/js?key={apiKey}&v=weekly{librariesParam}&callback=initMap' async defer></script>
</body>
</html>";
		}

		/// <summary>
		/// Selects the best available truck to highlight as recommended on the map.
		/// Uses the same ranking and tie-breakers as Day View candidate ordering:
		/// fit rank (Best=0, Good=1, Risky=2), then earliest AvailableAt, then PlateNumber.
		/// Blocked, Unknown, and unavailable trucks are excluded.
		/// Returns null when no job is selected or no suitable truck exists.
		/// </summary>
		private static Truck? PickRecommendedTruck(Job job, IEnumerable<Truck> trucks)
		{
			return trucks
				.Where(t => t.IsAvailable)
				.Select(t =>
				{
					var (tLabel, _) = DispatchFitService.GetTimeFit(job, t);
					var (rLabel, _) = DispatchFitService.GetRouteFit(job, t);
					var (eLabel, _) = DispatchFitService.GetEquipmentFit(job, t);
					string fit      = DispatchFitService.GetOverallFit(t, tLabel, rLabel, eLabel);
					return (truck: t, fit);
				})
				.Where(x => x.fit is "Best" or "Good" or "Risky")
				.OrderBy(x  => DispatchFitService.GetOverallFitSortRank(x.fit))
				.ThenBy(x   => x.truck.AvailableAt ?? DateTime.MaxValue)
				.ThenBy(x   => x.truck.PlateNumber, StringComparer.OrdinalIgnoreCase)
				.FirstOrDefault()
				.truck;
		}

		/// <summary>
		/// Builds the JSON array string that feeds window.dispatchTigerSetMarkers().
		/// Contains all truck markers (with vehicle SVG icons and status colours),
		/// plus pickup and delivery markers when a job is selected.
		/// </summary>
		private static string BuildMarkersJson(MainViewModel vm)
		{
			var sb = new StringBuilder();
			sb.Append('[');
			bool first = true;

			int stagedTruckId   = vm.StagedTruck?.Id   ?? -1;
			int selectedTruckId = vm.SelectedTruck?.Id ?? -1;
			var fitJob          = vm.SelectedJob;

			// Determine recommended truck once (only when a job is selected)
			int recommendedTruckId = fitJob != null
				? PickRecommendedTruck(fitJob, vm.AvailableTrucks)?.Id ?? -1
				: -1;

			// ── Truck markers (vehicle-shaped, status coloured, offset for stacking) ──
			var truckCoords = vm.AvailableTrucks
				.Select(t => (truck: t, coord: TryGetZoneCoordinate(t.CurrentLocation)))
				.Where(x => x.coord != null)
				.OrderBy(x => x.truck.Id)
				.ToList();

			var coordGroups = truckCoords
				.GroupBy(x => x.coord!.Value)
				.ToList();

			foreach (var group in coordGroups)
			{
				var members = group.ToList();
				int count   = members.Count;

				for (int idx = 0; idx < count; idx++)
				{
					var truck = members[idx].truck;
					var (baseLat, baseLng) = group.Key;
					var (lat, lng) = ApplyMarkerOffset(baseLat, baseLng, idx, count);

					string fitLabel  = "";
					string fitReason = "";
					if (fitJob != null)
					{
						var (tLabel, tDetail) = DispatchFitService.GetTimeFit(fitJob, truck);
						var (rLabel, rDetail) = DispatchFitService.GetRouteFit(fitJob, truck);
						var (eLabel, _)       = DispatchFitService.GetEquipmentFit(fitJob, truck);
						fitLabel  = DispatchFitService.GetOverallFit(truck, tLabel, rLabel, eLabel);
						fitReason = BuildFitReason(tLabel, tDetail, rDetail);
					}

					string color = truck.Id == stagedTruckId   ? "#FFD700"
								 : truck.Id == selectedTruckId ? "#FFD700"
								 : truck.IsAvailable           ? "#5B9BD5"
								 :                               "#888888";

					bool isRecommended = truck.Id == recommendedTruckId;

					string label   = EscapeJs(BuildTruckLabel(truck, fitLabel, fitReason));
					string iconUrl = GetVehicleMarkerSvg(truck, color, isRecommended);

					// Recommended markers use a 56×40 SVG; pass icon dimensions so the
					// JS side can set scaledSize / element size correctly.
					string iconW = isRecommended ? "56" : "48";
					string iconH = isRecommended ? "40" : "32";

					if (!first) sb.Append(',');
					first = false;
					sb.Append($"{{\"lat\":{lat:F6},\"lng\":{lng:F6}," +
							  $"\"label\":\"{label}\",\"color\":\"{color}\"," +
							  $"\"icon\":\"{iconUrl}\"," +
							  $"\"iconW\":{iconW},\"iconH\":{iconH}," +
							  $"\"type\":\"truck\",\"truckId\":{truck.Id}}}");
				}
			}

			// ── Pickup / Delivery markers (only when a job is selected) ──────────
			var job = vm.SelectedJob;
			if (job != null)
			{
				var pickupCoord = TryGetZoneCoordinate(job.PickupLocation?.Zone)
							   ?? TryGetZoneCoordinate(job.PickupLocation?.Address)
							   ?? TryGetZoneCoordinate(job.PickupAddress);
				if (pickupCoord != null)
				{
					if (!first) sb.Append(',');
					first = false;
					string lbl = EscapeJs(BuildPickupLabel(job));
					sb.Append($"{{\"lat\":{pickupCoord.Value.Lat},\"lng\":{pickupCoord.Value.Lng}," +
							  $"\"label\":\"{lbl}\",\"color\":\"#34A853\",\"type\":\"pickup\"}}");
				}

				var deliveryCoord = TryGetZoneCoordinate(job.DeliveryLocation?.Zone)
								 ?? TryGetZoneCoordinate(job.DeliveryLocation?.Address)
								 ?? TryGetZoneCoordinate(job.DeliveryAddress);
				if (deliveryCoord != null)
				{
					if (!first) sb.Append(',');
					string lbl = EscapeJs(BuildDeliveryLabel(job));
					sb.Append($"{{\"lat\":{deliveryCoord.Value.Lat},\"lng\":{deliveryCoord.Value.Lng}," +
							  $"\"label\":\"{lbl}\",\"color\":\"#EA4335\",\"type\":\"delivery\"}}");
				}
			}

			sb.Append(']');
			return sb.ToString();
		}

		/// <summary>
		/// Builds the JSON object that describes the straight-line route preview for the
		/// selected job.  Returns the string "null" when no job is selected or when
		/// neither pickup nor delivery coordinates can be resolved.
		///
		/// Truck source priority:
		///   1. vm.StagedTruck   — isStaged:true  (draws stronger dashed line)
		///   2. PickRecommendedTruck() — isStaged:false
		///   3. None             — truckToPickup leg is omitted
		///
		/// These are straight-line visual previews only.  No Directions/Routes API.
		/// </summary>
		private static string BuildRoutePreviewJson(MainViewModel vm)
		{
			var job = vm.SelectedJob;
			if (job == null) return "null";

			var pickupCoord = TryGetZoneCoordinate(job.PickupLocation?.Zone)
						   ?? TryGetZoneCoordinate(job.PickupLocation?.Address)
						   ?? TryGetZoneCoordinate(job.PickupAddress);

			var deliveryCoord = TryGetZoneCoordinate(job.DeliveryLocation?.Zone)
							 ?? TryGetZoneCoordinate(job.DeliveryLocation?.Address)
							 ?? TryGetZoneCoordinate(job.DeliveryAddress);

			// Resolve which truck to draw the deadhead leg from
			Truck? routeTruck  = vm.StagedTruck ?? PickRecommendedTruck(job, vm.AvailableTrucks);
			bool   isStaged    = vm.StagedTruck != null;
			var    truckCoord  = routeTruck != null ? TryGetZoneCoordinate(routeTruck.CurrentLocation) : null;

			// Need at least one leg to produce a non-null result
			if (pickupCoord == null && deliveryCoord == null) return "null";

			var sb = new StringBuilder("{");
			bool first = true;

			// truck→pickup leg (only when truck coord AND pickup coord are both known)
			if (truckCoord != null && pickupCoord != null)
			{
				sb.Append($"\"truckToPickup\":"
					   + $"{{\"fromLat\":{truckCoord.Value.Lat:F6},\"fromLng\":{truckCoord.Value.Lng:F6},"
					   + $"\"toLat\":{pickupCoord.Value.Lat:F6},\"toLng\":{pickupCoord.Value.Lng:F6},"
					   + $"\"isStaged\":{(isStaged ? "true" : "false")}}}");
				first = false;
			}

			// pickup→delivery leg (only when both coords are known)
			if (pickupCoord != null && deliveryCoord != null)
			{
				if (!first) sb.Append(',');
				sb.Append($"\"pickupToDelivery\":"
					   + $"{{\"fromLat\":{pickupCoord.Value.Lat:F6},\"fromLng\":{pickupCoord.Value.Lng:F6},"
					   + $"\"toLat\":{deliveryCoord.Value.Lat:F6},\"toLng\":{deliveryCoord.Value.Lng:F6}}}");
			}

			sb.Append('}');
			return sb.ToString();
		}


        // ── Vehicle marker icon ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns a data URL containing an inline SVG vehicle silhouette.
        /// Shape is chosen from VehicleType; fill color comes from status.
        /// When isRecommended is true the viewBox is enlarged to 56×40 and an
        /// orange ellipse halo ring is drawn behind the vehicle to identify it
        /// as the map's top recommended truck for the selected job.
        /// </summary>
        private static string GetVehicleMarkerSvg(Truck truck, string color, bool isRecommended = false)
        {
            string svgBody = GetVehicleSvgBody(truck?.VehicleType);

            string svg;
            if (isRecommended)
            {
                // Enlarged canvas: 56 wide × 40 tall.
                // Vehicle shapes are authored in a 48×32 space; offset them by (4,4)
                // so they sit centred inside the larger viewBox, leaving room for the ring.
                svg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 56 40' width='56' height='40'>"
                    // Outer glow ring — drawn first so it sits behind the vehicle
                    + "<ellipse cx='28' cy='20' rx='26' ry='16' fill='none' stroke='%23FFA500' stroke-width='3' opacity='0.92'/>"
                    // Inner ring for depth
                    + "<ellipse cx='28' cy='20' rx='22' ry='13' fill='none' stroke='%23FFD700' stroke-width='1.2' opacity='0.55'/>"
                    // Vehicle group — translated 4px right, 4px down to centre in larger canvas
                    + $"<g transform='translate(4,4)' fill='{color}' stroke='%23ffffff' stroke-width='0.8' stroke-linejoin='round'>"
                    + svgBody
                    + "</g></svg>";
            }
            else
            {
                // Standard 48×32 canvas
                svg = $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 48 32' width='48' height='32'>"
                    + $"<g fill='{color}' stroke='%23ffffff' stroke-width='0.8' stroke-linejoin='round'>"
                    + svgBody
                    + "</g></svg>";
            }

            // data URLs don't need the SVG encoded when the content is ASCII-safe;
            // percent-encode only the chars that break URL parsing.
            string encoded = svg.Replace("#", "%23").Replace("'", "%27");

            return "data:image/svg+xml," + encoded;
        }

        /// <summary>
        /// Returns the inner SVG path/rect elements that form the vehicle silhouette.
        /// Coordinate space: 48 wide × 32 tall. Wheel circles are drawn last so they
        /// appear on top of the body fill.
        /// </summary>
        private static string GetVehicleSvgBody(string? vehicleType)
        {
            string t = vehicleType?.Trim() ?? string.Empty;

            // Van / Cargo Van — rounded, tall-nosed panel-van profile
            if (t.Contains("Van", StringComparison.OrdinalIgnoreCase))
                return
                    // body + cab
                    "<rect x='1' y='8' width='42' height='16' rx='3'/>"
                    // windscreen notch
                    + "<rect x='33' y='10' width='8' height='7' rx='1' fill='%23ffffff66' stroke='none'/>"
                    // bumper
                    + "<rect x='39' y='20' width='5' height='3' rx='1'/>"
                    // rear panel
                    + "<rect x='2' y='9' width='4' height='14' rx='1'/>"
                    // wheels
                    + "<circle cx='10' cy='26' r='4' stroke-width='1'/>"
                    + "<circle cx='36' cy='26' r='4' stroke-width='1'/>"
                    + "<circle cx='10' cy='26' r='1.5' fill='%23ffffff88' stroke='none'/>"
                    + "<circle cx='36' cy='26' r='1.5' fill='%23ffffff88' stroke='none'/>"
                    // rear door line
                    + "<line x1='8' y1='8' x2='8' y2='24' stroke='%23ffffff55' stroke-width='0.7'/>";

            // Box Truck — tall square cargo box, short cab in front
            if (t.Contains("Box", StringComparison.OrdinalIgnoreCase))
                return
                    // cargo box
                    "<rect x='1' y='5' width='28' height='19' rx='1'/>"
                    // cab
                    + "<path d='M29 12 L29 24 L44 24 L44 14 L38 12 Z'/>"
                    // cab windscreen
                    + "<path d='M31 13 L37.5 13 L43 15 L43 21 L31 21 Z' fill='%23ffffff55' stroke='none'/>"
                    // bumper
                    + "<rect x='40' y='22' width='5' height='3' rx='1'/>"
                    // box door line
                    + "<line x1='28' y1='5' x2='28' y2='24' stroke='%23ffffff55' stroke-width='0.8'/>"
                    // wheels
                    + "<circle cx='9' cy='26' r='4' stroke-width='1'/>"
                    + "<circle cx='37' cy='26' r='4' stroke-width='1'/>"
                    + "<circle cx='9' cy='26' r='1.5' fill='%23ffffff88' stroke='none'/>"
                    + "<circle cx='37' cy='26' r='1.5' fill='%23ffffff88' stroke='none'/>";

            // Flatbed — low, long flat deck; compact cab
            if (t.Contains("Flatbed", StringComparison.OrdinalIgnoreCase))
                return
                    // flat deck
                    "<rect x='1' y='18' width='42' height='5' rx='1'/>"
                    // cab
                    + "<path d='M26 10 L26 18 L44 18 L44 12 L38 10 Z'/>"
                    // cab windscreen
                    + "<path d='M28 11 L37 11 L43 13 L43 17 L28 17 Z' fill='%23ffffff55' stroke='none'/>"
                    // bumper
                    + "<rect x='40' y='21' width='5' height='2' rx='1'/>"
                    // stake posts
                    + "<rect x='4'  y='14' width='1.5' height='4'/>"
                    + "<rect x='12' y='14' width='1.5' height='4'/>"
                    + "<rect x='20' y='14' width='1.5' height='4'/>"
                    // wheels
                    + "<circle cx='9'  cy='26' r='4' stroke-width='1'/>"
                    + "<circle cx='37' cy='26' r='4' stroke-width='1'/>"
                    + "<circle cx='9'  cy='26' r='1.5' fill='%23ffffff88' stroke='none'/>"
                    + "<circle cx='37' cy='26' r='1.5' fill='%23ffffff88' stroke='none'/>";

            // Generic Truck / Truck / unknown — classic cab-over with medium trailer
            return
                // trailer
                "<rect x='1' y='9' width='28' height='15' rx='1'/>"
                // cab
                + "<path d='M29 11 L29 24 L44 24 L44 13 L39 11 Z'/>"
                // cab windscreen
                + "<path d='M31 12 L38 12 L43 14 L43 21 L31 21 Z' fill='%23ffffff55' stroke='none'/>"
                // bumper
                + "<rect x='40' y='22' width='5' height='3' rx='1'/>"
                // connector
                + "<rect x='27' y='15' width='4' height='3' rx='0.5'/>"
                // wheels — rear dual
                + "<circle cx='8'  cy='26' r='4' stroke-width='1'/>"
                + "<circle cx='17' cy='26' r='4' stroke-width='1'/>"
                + "<circle cx='37' cy='26' r='4' stroke-width='1'/>"
                + "<circle cx='8'  cy='26' r='1.5' fill='%23ffffff88' stroke='none'/>"
                + "<circle cx='17' cy='26' r='1.5' fill='%23ffffff88' stroke='none'/>"
                + "<circle cx='37' cy='26' r='1.5' fill='%23ffffff88' stroke='none'/>";
        }

        // ── Label builders ────────────────────────────────────────────────────────

        private static string BuildTruckLabel(Truck truck, string fitLabel = "", string fitReason = "")
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
            if (!string.IsNullOrWhiteSpace(fitLabel))
            {
                sb.Append("\nFit: ").Append(fitLabel);
                if (!string.IsNullOrWhiteSpace(fitReason))
                    sb.Append("\nWhy: ").Append(fitReason);
            }
            return sb.ToString();
        }

        private static string BuildPickupLabel(Job job)
        {
            var sb = new StringBuilder("\U0001F4E6 Pickup");
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

        // ── JS string escape ────────────────────────────────────────────────────────────

        private static string BuildFitReason(string timeFitLabel, string? timeFitDetail, string? routeFitDetail)
        {
            var parts = new List<string>();

            if (timeFitDetail != null && (timeFitLabel == "Good" || timeFitLabel.StartsWith("Tight", StringComparison.Ordinal)))
            {
                // Extract pickup/delivery minutes from "pickup slack X; delivery slack Y"
                string pToken = ExtractToken(timeFitDetail, "pickup slack");
                string dToken = ExtractToken(timeFitDetail, "delivery slack");
                if (!string.IsNullOrEmpty(pToken)) parts.Add($"pickup {pToken}");
                if (!string.IsNullOrEmpty(dToken)) parts.Add($"delivery {dToken}");
            }
            else if (timeFitLabel.StartsWith("Late ", StringComparison.Ordinal))
            {
                parts.Add(timeFitLabel.ToLowerInvariant());
            }

            if (routeFitDetail != null)
            {
                string emptyToken = ExtractEmptyPct(routeFitDetail);
                if (!string.IsNullOrEmpty(emptyToken)) parts.Add(emptyToken);
            }

            return string.Join(" \u00b7 ", parts);
        }

        private static string ExtractToken(string detail, string key)
        {
            int idx = detail.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return string.Empty;
            int start = idx + key.Length;
            while (start < detail.Length && detail[start] == ' ') start++;
            int end = detail.IndexOf(';', start);
            return (end < 0 ? detail[start..] : detail[start..end]).Trim();
        }

        private static string ExtractEmptyPct(string detail)
        {
            const string key = "empty ";
            int idx = detail.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return string.Empty;
            int start = idx + key.Length;
            int end = detail.IndexOf(';', start);
            string pct = (end < 0 ? detail[start..] : detail[start..end]).Trim();
            return string.IsNullOrEmpty(pct) ? string.Empty : $"empty {pct}";
        }

        private static string EscapeJs(string text)
            => text
               .Replace("\\", "\\\\")
               .Replace("'", "\\'")
               .Replace("\r", "")
               .Replace("\n", "\\n");
    }
}
