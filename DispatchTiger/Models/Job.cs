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

        public override string ToString() => $"Job {Id}: {Description}";
    }
}
