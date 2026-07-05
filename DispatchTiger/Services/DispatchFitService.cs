using System;
using DispatchTiger.Models;

namespace DispatchTiger.Services
{
    /// <summary>
    /// Pure, WPF-free dispatch-fit scoring helpers shared between DayView and MapView.
    /// Returns labels and detail strings only — callers supply their own color mapping.
    /// All scoring rules are identical to the original DayView implementation.
    /// </summary>
    public static class DispatchFitService
    {
        // ── EstimateTravelMinutes ────────────────────────────────────────────────────

        /// <summary>
        /// Returns a rough travel-time estimate in minutes between two zone strings.
        /// Zones are identified by case-insensitive substring match against known area names.
        /// Returns null when either location cannot be matched to a known zone.
        /// Same-zone trips return 10 minutes. The table is symmetric.
        /// </summary>
        public static int? EstimateTravelMinutes(string? from, string? to)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                return null;

            static string? MatchZone(string text)
            {
                string[] zones = ["Sidney", "Victoria", "Langford", "Nanaimo", "Duncan",
                                  "Saanich", "Colwood", "Oak Bay", "Esquimalt", "Sooke"];
                foreach (var zone in zones)
                    if (text.Contains(zone, StringComparison.OrdinalIgnoreCase))
                        return zone;
                return null;
            }

            var fromZone = MatchZone(from);
            var toZone   = MatchZone(to);

            if (fromZone is null || toZone is null)
                return null;
            if (fromZone == toZone)
                return 10;

            // Canonical key: alphabetical order so the table is symmetric
            string key = string.Compare(fromZone, toZone, StringComparison.Ordinal) < 0
                ? $"{fromZone}|{toZone}"
                : $"{toZone}|{fromZone}";

            return key switch
            {
                "Sidney|Victoria"     => 35,
                "Langford|Sidney"     => 45,
                "Saanich|Sidney"      => 25,
                "Langford|Victoria"   => 25,
                "Saanich|Victoria"    => 15,
                "Esquimalt|Victoria"  => 10,
                "Oak Bay|Victoria"    => 15,
                "Colwood|Langford"    => 10,
                "Langford|Sooke"      => 35,
                "Duncan|Nanaimo"      => 40,
                "Duncan|Victoria"     => 60,
                "Nanaimo|Victoria"    => 95,
                "Nanaimo|Sidney"      => 85,
                "Colwood|Victoria"    => 20,
                "Esquimalt|Langford"  => 20,
                "Duncan|Saanich"      => 55,
                "Duncan|Langford"     => 50,
                "Colwood|Sooke"       => 30,
                "Oak Bay|Saanich"     => 15,
                "Esquimalt|Oak Bay"   => 20,
                _                     => null
            };
        }

        // ── GetTimeFit ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Evaluates time feasibility for assigning the selected job to a truck.
        /// Runs through data-availability guards first, then calculates pickup/delivery
        /// slack using EstimateTravelMinutes. Returns a label and an optional detail string.
        /// </summary>
        public static (string Label, string? Detail) GetTimeFit(Job job, Truck truck)
        {
            // ── data-availability guards ──────────────────────────────────────────
            if (!truck.IsAvailable)
                return ("Blocked", null);
            if (!job.PickupWindowStart.HasValue || !job.PickupWindowEnd.HasValue)
                return ("Missing pickup window", null);
            if (!job.DeliveryWindowStart.HasValue || !job.DeliveryWindowEnd.HasValue)
                return ("Missing delivery window", null);
            if (!job.EstimatedPickupMinutes.HasValue)
                return ("Missing pickup duration", null);
            if (!job.EstimatedDeliveryMinutes.HasValue)
                return ("Missing delivery duration", null);
            if (!truck.AvailableAt.HasValue)
                return ("Missing truck availability", null);
            if (string.IsNullOrWhiteSpace(truck.CurrentLocation))
                return ("Missing truck location", null);
            if (string.IsNullOrWhiteSpace(job.PickupAddress))
                return ("Missing pickup address", null);
            if (string.IsNullOrWhiteSpace(job.DeliveryAddress))
                return ("Missing delivery address", null);

            int? toPickupMin   = EstimateTravelMinutes(truck.CurrentLocation, job.PickupAddress);
            int? toDeliveryMin = EstimateTravelMinutes(job.PickupAddress,     job.DeliveryAddress);

            if (!toPickupMin.HasValue || !toDeliveryMin.HasValue)
                return ("Route estimate missing", null);

            // ── timing calculation ────────────────────────────────────────────────
            var pickupArrival   = truck.AvailableAt.Value.AddMinutes(toPickupMin.Value);
            var pickupSlack     = job.PickupWindowEnd!.Value  - pickupArrival;
            var pickupComplete  = pickupArrival.AddMinutes(job.EstimatedPickupMinutes!.Value);
            var deliveryArrival = pickupComplete.AddMinutes(toDeliveryMin.Value);
            var deliverySlack   = job.DeliveryWindowEnd!.Value - deliveryArrival;

            // ── feasibility labels ────────────────────────────────────────────────
            string detail = $"pickup slack {FormatSlack(pickupSlack)}; delivery slack {FormatSlack(deliverySlack)}";

            if (pickupSlack < TimeSpan.Zero)
                return ($"Late pickup by {FormatLate(pickupSlack)}", detail);
            if (deliverySlack < TimeSpan.Zero)
                return ($"Late delivery by {FormatLate(deliverySlack)}", detail);
            if (pickupSlack <= TimeSpan.FromMinutes(20))
                return ("Tight pickup", detail);
            if (deliverySlack <= TimeSpan.FromMinutes(20))
                return ("Tight delivery", detail);

            return ("Good", detail);
        }

        // ── GetRouteFit ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Judges basic route efficiency for a job/truck pair using deadhead vs loaded distance.
        /// DeadheadRatio = toPickup / (toPickup + pickupToDelivery).
        /// Returns a label and optional detail string.
        /// </summary>
        public static (string Label, string? Detail) GetRouteFit(Job job, Truck truck)
        {
            // ── data-availability guards ──────────────────────────────────────────
            if (!truck.IsAvailable)
                return ("Blocked", null);
            if (string.IsNullOrWhiteSpace(truck.CurrentLocation))
                return ("Missing truck location", null);
            if (string.IsNullOrWhiteSpace(job.PickupAddress))
                return ("Missing pickup address", null);
            if (string.IsNullOrWhiteSpace(job.DeliveryAddress))
                return ("Missing delivery address", null);

            int? deadheadMin = EstimateTravelMinutes(truck.CurrentLocation, job.PickupAddress);
            int? loadedMin   = EstimateTravelMinutes(job.PickupAddress,     job.DeliveryAddress);

            if (!deadheadMin.HasValue || !loadedMin.HasValue)
                return ("Route estimate missing", null);

            int total = deadheadMin.Value + loadedMin.Value;
            if (total <= 0)
                return ("Route estimate missing", null);

            // ── efficiency classification ─────────────────────────────────────────
            double ratio  = (double)deadheadMin.Value / total;
            int    pct    = (int)Math.Round(ratio * 100);
            string detail = $"deadhead {deadheadMin} min; loaded {loadedMin} min; empty {pct}%";

            if (ratio >= 0.60)
                return ("High deadhead", detail);
            if (ratio >= 0.40)
                return ("Acceptable", detail);

            return ("Efficient", detail);
        }

        // ── GetEquipmentFit ──────────────────────────────────────────────────────────

        /// <summary>
        /// Compares Job.RequiredEquipment against Truck.VehicleType (case-insensitive).
        /// Returns a label and an optional compact reason token.
        /// </summary>
        public static (string Label, string? Reason) GetEquipmentFit(Job job, Truck truck)
        {
            if (string.IsNullOrWhiteSpace(job.RequiredEquipment))
                return ("Not specified", null);

            if (string.IsNullOrWhiteSpace(truck.VehicleType))
                return ("Truck type unknown", $"Requires {job.RequiredEquipment}");

            if (truck.VehicleType.Equals(job.RequiredEquipment, StringComparison.OrdinalIgnoreCase))
                return ("Match", $"Equipment match: {job.RequiredEquipment}");

            return ("Mismatch",
                $"Equipment mismatch: requires {job.RequiredEquipment}, truck is {truck.VehicleType}");
        }

        // ── GetOverallFit ────────────────────────────────────────────────────────────

        /// <summary>
        /// Combines Time Fit, Route Fit, and Equipment Fit into a single overall dispatch recommendation.
        /// Rules are evaluated in priority order; the first match wins.
        /// Returns only the label string — callers supply color mapping.
        /// </summary>
        public static string GetOverallFit(
            Truck truck, string timeFitLabel, string routeFitLabel, string equipmentFitLabel)
        {
            // Rule 0: equipment mismatch makes the job impossible for this truck
            if (equipmentFitLabel == "Mismatch")
                return "Blocked";

            // Rule 1: truck is physically unavailable
            if (!truck.IsAvailable)
                return "Blocked";

            // Rule 2: timing makes the job impossible
            if (timeFitLabel.StartsWith("Late ", StringComparison.Ordinal))
                return "Blocked";

            // Rule 3: required job timing data is absent
            if (timeFitLabel.StartsWith("Missing", StringComparison.Ordinal))
                return "Unknown";

            // Rule 4: required route data is absent
            if (routeFitLabel.StartsWith("Missing", StringComparison.Ordinal) ||
                routeFitLabel == "Route estimate missing")
                return "Unknown";

            // Rule 5: timing is feasible but very tight
            if (timeFitLabel is "Tight pickup" or "Tight delivery")
                return "Risky";

            // Rules 6-8: Good timing — differentiate by route efficiency
            if (timeFitLabel == "Good")
            {
                return routeFitLabel switch
                {
                    "Efficient"     => "Best",
                    "Acceptable"    => "Good",
                    "High deadhead" => "Poor",
                    _               => "Candidate"
                };
            }

            // Rule 9: fallback
            return "Candidate";
        }

        // ── GetOverallFitSortRank ────────────────────────────────────────────────────

        /// <summary>
        /// Returns a sort rank for an Overall Fit label so candidates are displayed
        /// best-first. Lower rank = displayed higher in the list.
        /// </summary>
        public static int GetOverallFitSortRank(string fitLabel) => fitLabel switch
        {
            "Best"      => 0,
            "Good"      => 1,
            "Risky"     => 2,
            "Candidate" => 3,
            "Poor"      => 4,
            "Unknown"   => 5,
            "Blocked"   => 6,
            _           => 99
        };

        // ── Private helpers ──────────────────────────────────────────────────────────

        private static string FormatSlack(TimeSpan slack)
        {
            var abs = slack.Duration();
            return abs.TotalHours >= 1
                ? $"{(int)abs.TotalHours}h {abs.Minutes}m"
                : $"{(int)abs.TotalMinutes} min";
        }

        private static string FormatLate(TimeSpan negativeSlack) => FormatSlack(negativeSlack);
    }
}
