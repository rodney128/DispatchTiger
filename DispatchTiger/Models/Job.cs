using System;

namespace DispatchTiger.Models
{
    /// <summary>
    /// Represents a job or delivery task in the dispatch system.
    /// </summary>
    public class Job
    {
        public int Id { get; set; }
        public required string Description { get; set; }
        public string? PickupAddress { get; set; }
        public string? DeliveryAddress { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? ScheduledDate { get; set; }
        public int? ScheduledTime { get; set; } // Hour (0-23) for dispatch board scheduling
        public DateTime? CompletedDate { get; set; }
        public int? TruckId { get; set; }
        public Truck? Truck { get; set; }
        public DispatchStatus Status { get; set; } = DispatchStatus.Unassigned;
        public int? Priority { get; set; }
        public string? Notes { get; set; }

        // Pickup/delivery time-window fields — real dispatch timing constraints
        public DateTime? PickupWindowStart { get; set; }
        public DateTime? PickupWindowEnd { get; set; }
        public DateTime? DeliveryWindowStart { get; set; }
        public DateTime? DeliveryWindowEnd { get; set; }
        public int? EstimatedPickupMinutes { get; set; }   // Expected loading duration
        public int? EstimatedDeliveryMinutes { get; set; } // Expected unloading duration

        /// <summary>Truck/equipment type required for this job, e.g. Flatbed, Box Truck, Reefer (null = no specific requirement).</summary>
        public string? RequiredEquipment { get; set; }

        public override string ToString() => $"Job {Id}: {Description}";
    }
}
