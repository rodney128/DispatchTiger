using System.Collections.Generic;
using System.Text;
using DispatchTiger.Models;
using DispatchTiger.ViewModels;

namespace DispatchTiger.Views.Map
{
    /// <summary>
    /// Builds the JSON used for the straight-line route preview and for the
    /// Zoom to Job bounds. Job bounds intentionally exclude fleet truck positions
    /// so the viewport frames only the selected job's pickup and delivery.
    /// </summary>
    internal static class MapRouteBuilder
    {
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
        internal static string BuildRoutePreviewJson(MainViewModel vm)
        {
            var job = vm.SelectedJob;
            if (job == null) return "null";

            var pickupCoord = MapMarkerBuilder.TryGetZoneCoordinate(job.PickupLocation?.Zone)
                           ?? MapMarkerBuilder.TryGetZoneCoordinate(job.PickupLocation?.Address)
                           ?? MapMarkerBuilder.TryGetZoneCoordinate(job.PickupAddress);

            var deliveryCoord = MapMarkerBuilder.TryGetZoneCoordinate(job.DeliveryLocation?.Zone)
                             ?? MapMarkerBuilder.TryGetZoneCoordinate(job.DeliveryLocation?.Address)
                             ?? MapMarkerBuilder.TryGetZoneCoordinate(job.DeliveryAddress);

            // Resolve which truck to draw the deadhead leg from:
            //   • job already assigned  → the assigned truck (post-assignment Map View context)
            //   • truck staged          → the staged truck
            //   • otherwise             → the recommended truck (planning preview)
            Truck? routeTruck = job.Status == DispatchStatus.Assigned
                ? job.Truck ?? vm.StagedTruck ?? MapMarkerBuilder.PickRecommendedTruck(job, vm.AvailableTrucks)
                : vm.StagedTruck ?? MapMarkerBuilder.PickRecommendedTruck(job, vm.AvailableTrucks);
            bool   isStaged    = vm.StagedTruck != null;
            var    truckCoord  = routeTruck != null ? MapMarkerBuilder.TryGetZoneCoordinate(routeTruck.CurrentLocation) : null;

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

        /// <summary>
        /// Returns a JSON array of lat/lng points for the selected job's
        /// pickup and delivery coordinates only (no trucks, no fleet).
        /// </summary>
        internal static string BuildJobBoundsJson(MainViewModel vm)
        {
            var job = vm.SelectedJob;
            if (job == null) return "[]";

            var pts = new List<(double Lat, double Lng)>();

            var pickup = MapMarkerBuilder.TryGetZoneCoordinate(job.PickupLocation?.Zone)
                      ?? MapMarkerBuilder.TryGetZoneCoordinate(job.PickupLocation?.Address)
                      ?? MapMarkerBuilder.TryGetZoneCoordinate(job.PickupAddress);
            if (pickup != null) pts.Add(pickup.Value);

            var delivery = MapMarkerBuilder.TryGetZoneCoordinate(job.DeliveryLocation?.Zone)
                        ?? MapMarkerBuilder.TryGetZoneCoordinate(job.DeliveryLocation?.Address)
                        ?? MapMarkerBuilder.TryGetZoneCoordinate(job.DeliveryAddress);
            if (delivery != null) pts.Add(delivery.Value);

            if (pts.Count == 0) return "[]";

            var sb = new StringBuilder("[");
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"{{\"lat\":{pts[i].Lat:F6},\"lng\":{pts[i].Lng:F6}}}");
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
