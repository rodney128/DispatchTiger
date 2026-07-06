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

        // Load/weight fields — basic capacity utilisation without multi-stop routing
        /// <summary>Estimated or declared load weight in kilograms (null = unknown).</summary>
        public int? LoadWeightKg { get; set; }

        /// <summary>Number of discrete load units (pallets, boxes, crates — null = unknown).</summary>
        public int? LoadUnits { get; set; }

        // Company references — additive; existing address string fields are unchanged
        public int?     CustomerId { get; set; }
        public Company? Customer   { get; set; }

        public int?     ShipperId  { get; set; }
        public Company? Shipper    { get; set; }

        public int?     ReceiverId { get; set; }
        public Company? Receiver   { get; set; }

        // Location references — when set, PickupAddress/DeliveryAddress are overridden from the location
        public int?             PickupLocationId  { get; set; }
        public CompanyLocation? PickupLocation    { get; set; }

        public int?             DeliveryLocationId { get; set; }
        public CompanyLocation? DeliveryLocation   { get; set; }

        /// <summary>
        /// Short display identity that distinguishes jobs with identical descriptions.
        /// Format: "{Description} #{Id}"  e.g. "Bulk delivery #17"
        /// </summary>
        public string DisplayName => $"{Description} #{Id}";

        public override string ToString() => $"Job {Id}: {Description}";
    }
}
