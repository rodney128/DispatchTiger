using System;

namespace DispatchTiger.Models
{
    /// <summary>
    /// Represents the assignment of a job to a truck.
    /// </summary>
    public class Assignment
    {
        public int Id { get; set; }
        public int JobId { get; set; }
        public Job? Job { get; set; }
        public int TruckId { get; set; }
        public Truck? Truck { get; set; }
        public DateTime AssignedDate { get; set; } = DateTime.Now;
        public DateTime? UnassignedDate { get; set; }

        public override string ToString() => $"Assignment {Id}: Job {JobId} -> Truck {TruckId}";
    }
}
