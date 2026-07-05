using System;

namespace DispatchTiger.Models
{
    /// <summary>
    /// Represents a truck or vehicle in the dispatch system.
    /// </summary>
    public class Truck
    {
        public int Id { get; set; }
        public required string PlateNumber { get; set; }
        public string? VehicleType { get; set; }
        public int? DriverId { get; set; }
        public Driver? Driver { get; set; }
        public int? Capacity { get; set; }
        public bool IsAvailable { get; set; } = true;

        /// <summary>When the truck is expected to be free for another job (null = unknown).</summary>
        public DateTime? AvailableAt { get; set; }

        /// <summary>Known or estimated current location of the truck (null = unknown).</summary>
        public string? CurrentLocation { get; set; }

        public override string ToString() => $"{PlateNumber} (ID: {Id})";
    }
}
