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

        public override string ToString() => $"{PlateNumber} (ID: {Id})";
    }
}
