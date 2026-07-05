namespace DispatchTiger.Models
{
    /// <summary>
    /// Represents a business entity that can act as a customer, shipper, and/or receiver.
    /// Role flags (IsCustomer, IsShipper, IsReceiver) replace separate model types.
    /// </summary>
    public class Company
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        // Role flags — a company may hold any combination of roles
        public bool IsCustomer { get; set; }
        public bool IsShipper  { get; set; }
        public bool IsReceiver { get; set; }

        public string? PrimaryContactName { get; set; }
        public string? Phone              { get; set; }
        public string? Email              { get; set; }

        public string? DefaultAddress     { get; set; }
        public string? Notes              { get; set; }

        public string? PreferredEquipment { get; set; }
        public int     Priority           { get; set; }

        public override string ToString() => Name;
    }
}
