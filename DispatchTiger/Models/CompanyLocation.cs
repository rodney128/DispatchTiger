namespace DispatchTiger.Models
{
    /// <summary>
    /// A physical site belonging to a company where goods can be picked up or delivered.
    /// </summary>
    public class CompanyLocation
    {
        public int Id { get; set; }

        public int      CompanyId { get; set; }
        public Company? Company   { get; set; }

        /// <summary>Short display name for the location, e.g. "Main Yard" or "Store Dock".</summary>
        public string Name { get; set; } = "";

        public string? Address     { get; set; }
        public string? City        { get; set; }
        /// <summary>Zone name used by EstimateTravelMinutes (Sidney, Victoria, Langford, …).</summary>
        public string? Zone        { get; set; }

        public string? ContactName { get; set; }
        public string? Phone       { get; set; }
        public string? Notes       { get; set; }

        public bool CanPickup  { get; set; }
        public bool CanDeliver { get; set; }

        public override string ToString() => $"{Company?.Name} — {Name}";
    }
}
