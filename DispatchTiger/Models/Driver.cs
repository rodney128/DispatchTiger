namespace DispatchTiger.Models
{
    /// <summary>
    /// Represents a driver in the dispatch system.
    /// </summary>
    public class Driver
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public bool IsActive { get; set; } = true;

        public override string ToString() => $"{Name} (ID: {Id})";
    }
}
