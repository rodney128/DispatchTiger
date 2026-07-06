using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DispatchTiger.Models;
using DispatchTiger.Services;
using DispatchTiger.ViewModels;

namespace DispatchTiger.Views.Map
{
    /// <summary>
    /// Builds the marker JSON that feeds window.dispatchTigerSetMarkers(), plus the
    /// supporting pure helpers: zone coordinate lookup, marker offset spreading,
    /// vehicle SVG icons, InfoWindow label text, recommended-truck selection, and
    /// JS string escaping. All members are pure and side-effect free.
    /// </summary>
    internal static class MapMarkerBuilder
    {
        // ── Zone coordinate lookup ────────────────────────────────────────────────────────────────────────────────────────────────────────

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
        internal static (double Lat, double Lng)? TryGetZoneCoordinate(string? zoneOrAddress)
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

        // ── Recommended truck selection ───────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Selects the best available truck to highlight as recommended on the map.
        /// Uses the same ranking and tie-breakers as Day View candidate ordering:
        /// fit rank (Best=0, Good=1, Risky=2), then earliest AvailableAt, then PlateNumber.
        /// Blocked, Unknown, and unavailable trucks are excluded.
        /// Returns null when no job is selected or no suitable truck exists.
        /// </summary>
        internal static Truck? PickRecommendedTruck(Job job, IEnumerable<Truck> trucks)
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

        // ── Marker JSON builder ───────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the JSON array string that feeds window.dispatchTigerSetMarkers().
        /// Contains all truck markers (with vehicle SVG icons and status colours),
        /// plus pickup and delivery markers when a job is selected.
        /// </summary>
        internal static string BuildMarkersJson(MainViewModel vm)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            bool first = true;

            int stagedTruckId   = vm.StagedTruck?.Id   ?? -1;
            int selectedTruckId = vm.SelectedTruck?.Id ?? -1;
            var fitJob          = vm.SelectedJob;

            // Truck assigned to the currently selected job, when that job is already
            // assigned (post-assignment Map View context). Rendered green so the
            // dispatcher can see which truck just took the selected job.
            int assignedSelectedTruckId =
                fitJob != null && fitJob.Status == DispatchStatus.Assigned
                    ? fitJob.Truck?.Id ?? -1
                    : -1;

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

                    string color = truck.Id == stagedTruckId           ? "#FFD700"   // gold   — staged for assignment
                                 : truck.Id == assignedSelectedTruckId ? "#3FB950"   // green  — assigned to the selected job
                                 : truck.Id == selectedTruckId         ? "#7EC4CF"   // cyan   — selected, not staged
                                 : truck.IsAvailable                   ? "#5B9BD5"   // blue   — available
                                 :                                       "#888888";  // gray   — unavailable

                    bool isRecommended = truck.Id == recommendedTruckId;

                    string label   = EscapeJs(BuildTruckLabel(truck, fitLabel, fitReason, fitJob));
                    string iconUrl = GetVehicleMarkerSvg(truck, color, isRecommended);

                    // Recommended markers use a 56×40 SVG; pass icon dimensions so the
                    // JS side can set scaledSize / element size correctly.
                    string iconW = isRecommended ? "56" : "48";
                    string iconH = isRecommended ? "40" : "32";

                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append($"{{\"lat\":{lat:F6},\"lng\":{lng:F6},\"label\":\"{label}\",\"color\":\"{color}\",\"icon\":\"{iconUrl}\",\"iconW\":{iconW},\"iconH\":{iconH},\"type\":\"truck\",\"truckId\":{truck.Id}}}");
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

        // ── Vehicle marker icon ───────────────────────────────────────────────────────────────────────────────────────────────────────────

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

        // ── Label builders ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        private static string BuildTruckLabel(Truck truck, string fitLabel = "", string fitReason = "", Job? selectedJob = null)
        {
            var sb = new StringBuilder();
            sb.Append("🚛 ").Append(truck.PlateNumber);
            if (!string.IsNullOrWhiteSpace(truck.VehicleType)) sb.Append('\n').Append(truck.VehicleType);
            if (truck.Driver != null) sb.Append('\n').Append(truck.Driver.Name);
            sb.Append('\n').Append(truck.IsAvailable ? "Available" : "Unavailable");
            if (truck.AvailableAt.HasValue) sb.Append(" from ").Append(truck.AvailableAt.Value.ToString("h:mm tt"));
            if (truck.Capacity.HasValue || truck.CapacityUnits.HasValue)
            {
                sb.Append("\nCap:");
                if (truck.Capacity.HasValue) sb.Append($" {truck.Capacity.Value:N0} kg");
                if (truck.CapacityUnits.HasValue) sb.Append($" / {truck.CapacityUnits.Value} units");
            }
            if (!string.IsNullOrWhiteSpace(fitLabel))
            { sb.Append("\nFit: ").Append(fitLabel);
              if (!string.IsNullOrWhiteSpace(fitReason)) sb.Append("\nWhy: ").Append(fitReason); }
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

        // ── JS string escape ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

        internal static string EscapeJs(string text)
            => text
               .Replace("\\", "\\\\")
               .Replace("'", "\\'")
               .Replace("\r", "")
               .Replace("\n", "\\n");
    }
}
