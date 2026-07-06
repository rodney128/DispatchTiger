using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DispatchTiger.Models;
using DispatchTiger.Services;
using DispatchTiger.ViewModels;
using DispatchTiger.Views.Map;
using Microsoft.Web.WebView2.Core;

namespace DispatchTiger.Views
{
    public partial class MapView : UserControl
    {
        private MainViewModel? _vm;
        private bool _webViewReady;
        private bool _mapHtmlLoaded;                                    // true once NavigationCompleted fires successfully
        private int? _lastAutoFitJobId;                                 // id of the last job auto-fit framed on selection; prevents re-framing the same job
        private System.Threading.CancellationTokenSource? _markerDebounce; // collapses rapid PropertyChanged firings
        private string? _mapSuccessMessage;                             // set after assignment; shown in toolbar until next job is selected

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

        // ── Event handlers ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private async void MapView_Loaded(object sender, RoutedEventArgs e)
        {
            await TryInitializeMapAsync();
        }

        private void RefreshMapButton_Click(object sender, RoutedEventArgs e)
        {
            FullReloadMap();
        }

        private async void ZoomToJobButton_Click(object sender, RoutedEventArgs e)
        {
            await ZoomToJobAsync();
        }

        /// <summary>
        /// Calls the dedicated JS zoom function with only the selected job’s
        /// pickup and delivery coordinates. Excludes all truck positions.
        /// </summary>
        private async Task ZoomToJobAsync()
        {
            if (!_mapHtmlLoaded || !_webViewReady || _vm?.SelectedJob == null) return;
            string boundsJson = MapRouteBuilder.BuildJobBoundsJson(_vm);
            if (boundsJson == "[]") return;
            string escaped = boundsJson.Replace("\\", "\\\\").Replace("'", "\\'");
            try { await MapWebView.ExecuteScriptAsync($"window.dispatchTigerZoomToJob('{escaped}');"); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[MapView] ZoomToJob script failed (WebView not ready?): {ex.Message}"); }
        }

        private void AssignStagedButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;

            // Delegate the full assignment workflow to the shared ViewModel method
            // (validate, promote staged truck, AssignJobCommand, StatusMessage, clear).
            // reselectAfterAssign:true keeps the assigned job + truck selected so the map
            // retains pickup/delivery/route and shows the assigned truck in green.
            var result = _vm.AssignStaged(reselectAfterAssign: true);
            if (!result.Success) return;

            // Job stays selected (now Assigned), so the render block shows the assigned
            // confirmation text and the AssignmentPanel hides on its own. Push immediately
            // (fitBounds:false so the viewport does not jump) to reflect the new state now.
            _ = PushMarkersAsync(fitBounds: false);
        }

        private void CancelStagingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            // Clear staging only; keep SelectedJob so the dispatcher can re-pick a truck.
            _vm.StagedTruck = null;
            // Vm_PropertyChanged fires → PushMarkersAsync handles toolbar/marker update.
        }

        // ── Map initialisation ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

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

        // ── Setup guide button handlers ────────────────────────────────────────────────────────────────────────────────────────────────────

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
        /// Does NOT execute AssignJobCommand — assignment is via the WPF overlay panel.
        /// </summary>
        private void HandleWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (_vm == null) return;

            try
            {
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

                // Set SelectedTruck — does NOT assign; assignment is via the WPF panel
                _vm.SelectedTruck = truck;

                var job = _vm.SelectedJob;
                if (!truck.IsAvailable)
                {
                    _vm.StatusMessage = $"Selected {truck.PlateNumber} from map — truck is unavailable. Review before assigning.";
                }
                else if (job != null)
                {
                    // Stage the truck — the WPF overlay panel will appear immediately
                    _vm.StagedTruck = truck;
                    _vm.StatusMessage = $"Staged {truck.PlateNumber} for {job.DisplayName}.";
                }
                else
                {
                    // No job selected - selection only, no staging
                    _vm.StatusMessage = $"Selected {truck.DisplayName} from map. Select an unassigned job first.";
                }

                // Toolbar truck status line
                UpdateTruckStatusText();

                // SelectedTruck and StagedTruck changes both fire Vm_PropertyChanged -> PushMarkersAsync.
                // UpdateTruckStatusText is called synchronously here so the toolbar updates
                // immediately without waiting for the debounced async push.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MapView] Malformed web message ignored: {ex.Message}");
            }
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(MainViewModel.SelectedJob)
                                   or nameof(MainViewModel.SelectedTruck)
                                   or nameof(MainViewModel.StagedTruck)
                                   or nameof(MainViewModel.StatusMessage)))
                return;

            // When Undo fires it sets StatusMessage — clear the success banner so the
            // toolbar reverts to the standard 'Select a job' or undo-confirmation text.
            if (e.PropertyName == nameof(MainViewModel.StatusMessage))
            {
                if (_mapSuccessMessage != null)
                {
                    _mapSuccessMessage = null;
                    // Refresh toolbar text on the UI thread without moving viewport.
                    Dispatcher.InvokeAsync(async () => await PushMarkersAsync(fitBounds: false));
                }
                return;
            }

            // Viewport rules:
            //   SelectedJob  → different job : fitBounds = true  (auto-fit once to the selected job's
            //                                  pickup + delivery only — no fleet truck markers)
            //   SelectedJob  → same job again : fitBounds = false (suppress re-binding noise)
            //   SelectedTruck / StagedTruck   : fitBounds = false (marker colour update only)
            bool fitBounds = false;
            if (e.PropertyName == nameof(MainViewModel.SelectedJob))
            {
                int? newJobId = _vm?.SelectedJob?.Id;
                // Only auto-fit when the job truly changed (null→job, job→null, job→differentJob).
                if (newJobId != _lastAutoFitJobId)
                {
                    fitBounds = true;
                    _lastAutoFitJobId = newJobId;    // record now so a superseded debounce tick can't re-trigger
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

        // ── Map refresh ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

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
            _lastAutoFitJobId = null;   // fresh page resets the viewport, so allow the current job to auto-fit again
            var apiKey = Environment.GetEnvironmentVariable("GOOGLE_MAPS_API_KEY") ?? "";
            MapWebView.NavigateToString(MapHtmlBuilder.BuildMapHtml(apiKey));
        }

        private void UpdateTruckStatusText()
        {
            var staged   = _vm?.StagedTruck;
            var selected = _vm?.SelectedTruck;
            var job      = _vm?.SelectedJob;

            if (staged != null)
            {
                var status = staged.IsAvailable ? "available" : "unavailable";
                MapTruckStatusText.Text = $"Staged: {staged.PlateNumber}  \u00b7  {status}";
                var colour = staged.IsAvailable
                    ? System.Windows.Media.Color.FromRgb(255, 215, 0)   // gold when available
                    : System.Windows.Media.Color.FromRgb(255, 140, 0);  // amber when unavailable
                MapTruckStatusText.Foreground = new System.Windows.Media.SolidColorBrush(colour);
            }
            else if (job != null && job.Status == DispatchStatus.Assigned
                     && selected != null && selected.Id == job.Truck?.Id)
            {
                // Post-assignment: the selected truck is the one assigned to the selected job.
                MapTruckStatusText.Text = $"Assigned: {selected.PlateNumber}  \u00b7  Undo available";
                MapTruckStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(63, 185, 80));   // green — assigned
            }
            else if (selected != null)
            {
                var status = selected.IsAvailable ? "available" : "unavailable";
                string suffix = job != null ? "  \u00b7  Click a truck marker to stage it" : "  \u00b7  Select an unassigned job first";
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

            string json       = MapMarkerBuilder.BuildMarkersJson(_vm);
            string routeJson  = MapRouteBuilder.BuildRoutePreviewJson(_vm);
            string fitArg     = fitBounds ? "true" : "false";
            // Escape both JSON strings for embedding as JS string literals
            string escaped      = json.Replace("\\", "\\\\").Replace("'", "\\'");
            string routeEscaped = routeJson == "null" ? "null" : $"'{routeJson.Replace("\\", "\\\\").Replace("'", "\\'")}'";
            string callJs       = $"window.dispatchTigerSetMarkers('{escaped}', {routeEscaped}, {fitArg});";

            try
            {
                await MapWebView.ExecuteScriptAsync(callJs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MapView] SetMarkers script failed (WebView navigating?): {ex.Message}");
            }

            // Update toolbar text (same as before, just no map reload)
            var job = _vm.SelectedJob;

            if (job != null)
            {
                // A job is active — clear any lingering success banner from the previous assignment.
                _mapSuccessMessage = null;
                if (job.Status == DispatchStatus.Assigned)
                {
                    // Post-assignment: job stays selected so the map keeps its context.
                    // Show a clear confirmation; the assigned truck marker is green.
                    string plate = job.Truck?.PlateNumber ?? _vm.SelectedTruck?.PlateNumber ?? "truck";
                    MapStatusText.Text = $"✓ Assigned {job.DisplayName} to {plate}. Undo available.";
                }
                else
                {
                    MapStatusText.Text = _vm.StagedTruck != null
                        ? $"Staged: {_vm.StagedTruck.PlateNumber} for {job.DisplayName}."
                        : $"Job selected: {job.DisplayName}. Click an available truck marker to stage it.";
                }
            }
            else
            {
                // No active job — show the success banner if present, otherwise generic hint.
                MapStatusText.Text = _mapSuccessMessage
                    ?? "Select an unassigned job on the left to show it on the map.";
            }

            // Assignment confirmation panel:
            // Visible only when a job is selected, a truck is staged, and the job is still unassigned.
            bool showAssign = job != null
                && _vm.StagedTruck != null
                && job.Status == DispatchStatus.Unassigned;

            if (showAssign)
            {
                string truckDisplay        = _vm.StagedTruck!.PlateNumber;
                AssignmentPanelText.Text   = $"Assign {truckDisplay} to {job!.DisplayName}?";
                AssignStagedButton.Content = $"✓ Assign {truckDisplay}";
                AssignmentPanel.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                AssignmentPanel.Visibility = System.Windows.Visibility.Collapsed;
            }

            if (job != null)
            {
                // Combined fit + route legend on a single line to reduce toolbar crowding.
                MapFitLegendText.Text = "Fit: ★ Best • Good ▲ Risky • Poor • Blocked (badge per marker)   |   Route (straight line): ┄┄ truck → pickup  ── pickup → delivery";
                MapFitLegendText.Visibility = System.Windows.Visibility.Visible;

                // Show Zoom to Job button so the dispatcher can recentre after panning
                ZoomToJobButton.Visibility = System.Windows.Visibility.Visible;

                // Recommended truck line
                var rec = MapMarkerBuilder.PickRecommendedTruck(job, _vm.AvailableTrucks);
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
                ZoomToJobButton.Visibility = System.Windows.Visibility.Collapsed;
                MapFitLegendText.Visibility = System.Windows.Visibility.Collapsed;
                MapRecommendedText.Visibility = System.Windows.Visibility.Collapsed;
            }

            UpdateTruckStatusText();
        }
    }
}
