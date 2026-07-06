using System;
using System.Collections.Generic;
using System.Linq;
using DispatchTiger.Models;

namespace DispatchTiger.Services
{
    /// <summary>
    /// Generates seed data for the dispatcher application.
    /// Provides 30 trucks, 30 drivers, and 100 jobs with realistic mixed data.
    /// </summary>
    public class SeedDataService
    {
        private static readonly Random Random = new(42); // Fixed seed for consistency

        private static readonly string?[] PickupAddresses =
        [
            "123 Beacon Ave, Sidney",
            "450 Douglas St, Victoria",
            "88 Goldstream Ave, Langford",
            "22 Terminal Ave, Nanaimo",
            "700 Canada Ave, Duncan",
            "3100 Blanshard St, Saanich",
            "1913 Sooke Rd, Colwood",
            "2200 Oak Bay Ave, Oak Bay",
            "1150 Esquimalt Rd, Esquimalt",
            "6700 Sooke Rd, Sooke",
            "9805 Third St, Sidney",
            null, // Some missing addresses
            "1234 Government St, Victoria",
            "560 Thetis Vale Cres, Langford",
            "40 Front St, Nanaimo",
        ];

        private static readonly string?[] DeliveryAddresses =
        [
            "800 Cloverdale Ave, Saanich",
            "2600 Rock Bay Ave, Victoria",
            "101 Helmcken Rd, Langford",
            "1200 Old Island Hwy, Colwood",
            "311 Trunk Rd, Duncan",
            "6675 Sooke Rd, Sooke",
            "1399 McKenzie Ave, Saanich",
            "2500 Hackett Cres, Sidney",
            "150 Craig St, Esquimalt",
            null, // Some missing addresses
            "4475 Victoria Ave, Nanaimo",
            "1170 Oak Bay Ave, Oak Bay",
        ];

        private static readonly string[] JobDescriptions = new[]
        {
            "Deliver package",
            "Pick up and deliver",
            "Urgent delivery",
            "Fragile items transport",
            "Bulk delivery",
            "Same-day delivery",
            "Scheduled delivery",
            "Special handling required",
            "Document delivery",
            "Equipment transport"
        };

        private static readonly string?[] VehicleTypes = new string?[]
        {
            "Van",
            "Truck",
            "Box Truck",
            "Flatbed",
            null, // Some missing vehicle types
            "Cargo Van"
        };

        private static readonly string[] TruckLocations = new[]
        {
            "Sidney", "Victoria", "Langford", "Nanaimo", "Duncan",
            "Saanich", "Colwood", "Oak Bay", "Esquimalt", "Sooke"
        };

        // Values intentionally overlap with VehicleTypes so matches are possible
        private static readonly string[] RequiredEquipmentOptions =
        [
            "Van", "Box Truck", "Flatbed", "Cargo Van", "Truck"
        ];

        private static readonly string[] DriverNames = new[]
        {
            "John Smith", "Maria Garcia", "James Johnson", "Patricia Lee", "Robert Brown",
            "Jennifer White", "Michael Davis", "Linda Martinez", "David Rodriguez", "Barbara Jones",
            "Richard Miller", "Susan Anderson", "Joseph Taylor", "Jessica Thomas", "Thomas Moore",
            "Sarah Jackson", "Charles Martin", "Karen Harris", "Christopher Thompson", "Nancy Garcia",
            "Daniel Martinez", "Cynthia Robinson", "Matthew Clark", "Catherine Lewis", "Anthony Walker",
            "Brenda Young", "Mark Hernandez", "Diane Hall", "Donald Allen", "Judy King"
        };

        private static readonly string[] PlateNumbers = new[]
        {
            "ABC123", "XYZ789", "DEF456", "GHI012", "JKL345", "MNO678", "PQR901", "STU234", "VWX567", "YZA890",
            "BCD012", "EFG345", "HIJ678", "KLM901", "NOP234", "QRS567", "TUV890", "WXY123", "ZAB456", "CDE789",
            "FGH012", "IJK345", "LMN678", "OPQ901", "RST234", "UVW567", "XYZ890", "ABC123", "DEF456", "GHI789"
        };

        /// <summary>
        /// Generates the 10 seed companies that serve as customers, shippers, and receivers.
        /// </summary>
        public static List<Company> GenerateCompanies()
        {
            return
            [
                new Company
                {
                    Id = 1, Name = "Island Freight Services",
                    IsCustomer = true, IsShipper = true,
                    PrimaryContactName = "Karen Wells", Phone = "250-555-0101",
                    Email = "dispatch@islandfreight.ca",
                    DefaultAddress = "9805 Third St, Sidney", Priority = 2
                },
                new Company
                {
                    Id = 2, Name = "Sidney Building Supply",
                    IsCustomer = true, IsShipper = true, IsReceiver = true,
                    PrimaryContactName = "Tom Arsenault", Phone = "250-555-0202",
                    Email = "orders@sidneybuild.ca",
                    DefaultAddress = "123 Beacon Ave, Sidney", Priority = 3
                },
                new Company
                {
                    Id = 3, Name = "Victoria Cold Storage",
                    IsShipper = true, IsReceiver = true,
                    PrimaryContactName = "Diane Chu", Phone = "250-555-0303",
                    Email = "ops@victoriacs.ca",
                    DefaultAddress = "2600 Rock Bay Ave, Victoria",
                    PreferredEquipment = "Reefer", Priority = 2
                },
                new Company
                {
                    Id = 4, Name = "Langford Hardware",
                    IsCustomer = true, IsReceiver = true,
                    PrimaryContactName = "Bruce Lafleur", Phone = "250-555-0404",
                    Email = "receiving@langfordhw.ca",
                    DefaultAddress = "88 Goldstream Ave, Langford", Priority = 3
                },
                new Company
                {
                    Id = 5, Name = "Nanaimo Marine Supply",
                    IsCustomer = true, IsShipper = true,
                    PrimaryContactName = "Sandra Okoro", Phone = "250-555-0505",
                    Email = "info@nanaimomarine.ca",
                    DefaultAddress = "22 Terminal Ave, Nanaimo", Priority = 2
                },
                new Company
                {
                    Id = 6, Name = "Duncan Farm Co-op",
                    IsShipper = true, IsReceiver = true,
                    PrimaryContactName = "Ray Holbrook", Phone = "250-555-0606",
                    Email = "coop@duncanfarm.ca",
                    DefaultAddress = "700 Canada Ave, Duncan", Priority = 1
                },
                new Company
                {
                    Id = 7, Name = "Saanich Distribution",
                    IsShipper = true, IsReceiver = true,
                    PrimaryContactName = "Lena Fitzgerald", Phone = "250-555-0707",
                    Email = "dock@saanichds.ca",
                    DefaultAddress = "3100 Blanshard St, Saanich", Priority = 2
                },
                new Company
                {
                    Id = 8, Name = "Colwood Construction",
                    IsCustomer = true, IsReceiver = true,
                    PrimaryContactName = "Gary Meston", Phone = "250-555-0808",
                    Email = "site@colwoodcon.ca",
                    DefaultAddress = "1913 Sooke Rd, Colwood", Priority = 3
                },
                new Company
                {
                    Id = 9, Name = "Oak Bay Retail",
                    IsCustomer = true, IsReceiver = true,
                    PrimaryContactName = "Fiona MacDougall", Phone = "250-555-0909",
                    Email = "purchasing@oakbayretail.ca",
                    DefaultAddress = "2200 Oak Bay Ave, Oak Bay", Priority = 2
                },
                new Company
                {
                    Id = 10, Name = "Esquimalt Shipyard",
                    IsCustomer = true, IsShipper = true, IsReceiver = true,
                    PrimaryContactName = "Carlos Reyes", Phone = "250-555-1010",
                    Email = "logistics@esquimaltsy.ca",
                    DefaultAddress = "1150 Esquimalt Rd, Esquimalt", Priority = 4
                },
            ];
        }

        /// <summary>
        /// Generates seed locations — at least one per company, some companies have multiple.
        /// Zone values match the zones recognised by EstimateTravelMinutes.
        /// </summary>
        public static List<CompanyLocation> GenerateLocations(List<Company> companies)
        {
            Company C(int id) => companies.First(c => c.Id == id);

            return
            [
                // Island Freight Services (Id 1) — shipper
                new CompanyLocation { Id =  1, CompanyId = 1, Company = C(1),
                    Name = "Sidney Dock",
                    Address = "9805 Third St, Sidney", City = "Sidney", Zone = "Sidney",
                    Phone = "250-555-0111", CanPickup = true },

                // Sidney Building Supply (Id 2) — shipper + receiver, two locations
                new CompanyLocation { Id =  2, CompanyId = 2, Company = C(2),
                    Name = "Main Yard",
                    Address = "123 Beacon Ave, Sidney", City = "Sidney", Zone = "Sidney",
                    Phone = "250-555-0212", CanPickup = true, CanDeliver = true },
                new CompanyLocation { Id =  3, CompanyId = 2, Company = C(2),
                    Name = "North Branch",
                    Address = "9805 Third St, Sidney", City = "Sidney", Zone = "Sidney",
                    Phone = "250-555-0213", CanPickup = true },

                // Victoria Cold Storage (Id 3) — shipper + receiver
                new CompanyLocation { Id =  4, CompanyId = 3, Company = C(3),
                    Name = "Cold Storage Dock",
                    Address = "2600 Rock Bay Ave, Victoria", City = "Victoria", Zone = "Victoria",
                    Phone = "250-555-0314", CanPickup = true, CanDeliver = true },

                // Langford Hardware (Id 4) — receiver only
                new CompanyLocation { Id =  5, CompanyId = 4, Company = C(4),
                    Name = "Store Dock",
                    Address = "88 Goldstream Ave, Langford", City = "Langford", Zone = "Langford",
                    Phone = "250-555-0415", CanDeliver = true },

                // Nanaimo Marine Supply (Id 5) — shipper only
                new CompanyLocation { Id =  6, CompanyId = 5, Company = C(5),
                    Name = "Marine Wharf",
                    Address = "22 Terminal Ave, Nanaimo", City = "Nanaimo", Zone = "Nanaimo",
                    Phone = "250-555-0516", CanPickup = true },

                // Duncan Farm Co-op (Id 6) — shipper + receiver
                new CompanyLocation { Id =  7, CompanyId = 6, Company = C(6),
                    Name = "Farm Gate",
                    Address = "700 Canada Ave, Duncan", City = "Duncan", Zone = "Duncan",
                    Phone = "250-555-0617", CanPickup = true, CanDeliver = true },

                // Saanich Distribution (Id 7) — shipper + receiver, two locations
                new CompanyLocation { Id =  8, CompanyId = 7, Company = C(7),
                    Name = "Main Hub",
                    Address = "3100 Blanshard St, Saanich", City = "Saanich", Zone = "Saanich",
                    Phone = "250-555-0718", CanPickup = true, CanDeliver = true },
                new CompanyLocation { Id =  9, CompanyId = 7, Company = C(7),
                    Name = "South Gate",
                    Address = "1399 McKenzie Ave, Saanich", City = "Saanich", Zone = "Saanich",
                    Phone = "250-555-0719", CanPickup = true },

                // Colwood Construction (Id 8) — receiver only
                new CompanyLocation { Id = 10, CompanyId = 8, Company = C(8),
                    Name = "Site Office",
                    Address = "1913 Sooke Rd, Colwood", City = "Colwood", Zone = "Colwood",
                    Phone = "250-555-0820", CanDeliver = true },

                // Oak Bay Retail (Id 9) — receiver only
                new CompanyLocation { Id = 11, CompanyId = 9, Company = C(9),
                    Name = "Receiving Bay",
                    Address = "2200 Oak Bay Ave, Oak Bay", City = "Oak Bay", Zone = "Oak Bay",
                    Phone = "250-555-0921", CanDeliver = true },

                // Esquimalt Shipyard (Id 10) — shipper + receiver, two locations
                new CompanyLocation { Id = 12, CompanyId = 10, Company = C(10),
                    Name = "Main Wharf",
                    Address = "1150 Esquimalt Rd, Esquimalt", City = "Esquimalt", Zone = "Esquimalt",
                    Phone = "250-555-1022", CanPickup = true, CanDeliver = true },
                new CompanyLocation { Id = 13, CompanyId = 10, Company = C(10),
                    Name = "Annex Gate",
                    Address = "150 Craig St, Esquimalt", City = "Esquimalt", Zone = "Esquimalt",
                    Phone = "250-555-1023", CanDeliver = true },
            ];
        }

        /// <summary>
        /// Generates seed drivers.
        /// </summary>
        public static List<Driver> GenerateDrivers(int count = 30)
        {
            var drivers = new List<Driver>();
            for (int i = 1; i <= count; i++)
            {
                drivers.Add(new Driver
                {
                    Id = i,
                    Name = DriverNames[i - 1],
                    PhoneNumber = Random.Next(100) < 20 ? null : $"555-{Random.Next(1000):D4}", // Some missing phone numbers
                    Email = Random.Next(100) < 15 ? null : $"driver{i}@dispatch.local",
                    IsActive = Random.Next(100) > 10 // 90% active
                });
            }
            return drivers;
        }

        /// <summary>
        /// Generates seed trucks.
        /// </summary>
        public static List<Truck> GenerateTrucks(int count = 30, List<Driver>? drivers = null)
        {
            drivers ??= GenerateDrivers(30);
            var trucks = new List<Truck>();

            // Deal drivers to trucks WITHOUT replacement so no two seeded trucks share a
            // driver (and therefore no duplicate active driver names). Shuffle the driver
            // ids once, then hand them out; when the pool is empty the remaining trucks are
            // simply left unassigned. This is demo-data realism only — no runtime rule.
            var driverPool = new Queue<int>(
                drivers.Select(d => d.Id).OrderBy(_ => Random.Next()));

            for (int i = 1; i <= count; i++)
            {
                // ~70% of trucks get a driver, but only while unique drivers remain.
                int? driverId = (Random.Next(100) < 70 && driverPool.Count > 0)
                    ? driverPool.Dequeue()
                    : null;

                trucks.Add(new Truck
                {
                    Id = i,
                    PlateNumber = PlateNumbers[i - 1],
                    VehicleType = VehicleTypes[Random.Next(VehicleTypes.Length)],
                    DriverId = driverId,
                    Driver = driverId.HasValue ? drivers.First(d => d.Id == driverId) : null,
                    Capacity = Random.Next(100) < 30 ? null : Random.Next(500, 5000), // weight capacity in kg
                    CapacityUnits = Random.Next(100) < 25 ? null : Random.Next(4, 33), // pallet/unit capacity
                    IsAvailable = Random.Next(100) > 20, // 80% available
                    // AvailableAt: ~70% of trucks have a known free time today
                    AvailableAt = Random.Next(100) < 70
                        ? DateTime.Today.AddHours(Random.Next(7, 18)).AddMinutes(Random.Next(0, 4) * 15)
                        : null,
                    // CurrentLocation: ~75% of trucks have a known location
                    CurrentLocation = Random.Next(100) < 75
                        ? TruckLocations[Random.Next(TruckLocations.Length)]
                        : null
                });
            }
            return trucks;
        }

        /// <summary>
        /// Generates seed jobs with assignments spread throughout the current month.
        /// </summary>
        public static (List<Job> Jobs, List<Assignment> Assignments) GenerateJobs(int count = 100, List<Truck>? trucks = null, List<Company>? companies = null, List<CompanyLocation>? locations = null)
        {
            trucks ??= GenerateTrucks(30);
            var jobs = new List<Job>();
            var assignments = new List<Assignment>();

            // Pre-compute role-filtered company lists for round-robin assignment
            var customerCompanies  = companies?.Where(c => c.IsCustomer).ToList() ?? [];
            var shipperCompanies   = companies?.Where(c => c.IsShipper).ToList()  ?? [];
            var receiverCompanies  = companies?.Where(c => c.IsReceiver).ToList() ?? [];
            var pickupLocations    = locations?.Where(l => l.CanPickup).ToList()  ?? [];
            var deliveryLocations  = locations?.Where(l => l.CanDeliver).ToList() ?? [];
            var today = DateTime.Now.Date;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            for (int i = 1; i <= count; i++)
            {
                var isAssigned = Random.Next(100) < 70; // 70% of jobs are assigned
                var truck = isAssigned ? trucks[Random.Next(trucks.Count)] : null;

                var scheduledDate = new DateTime(
                    today.Year,
                    today.Month,
                    Random.Next(1, (monthEnd - monthStart).Days + 2)
                );

                // Assign scheduled time for hourly dispatch board (6 AM to 8 PM = 6:00-20:00)
                var scheduledTime = Random.Next(6, 21); // Hours 6 through 20 (6AM to 8PM)

                var job = new Job
                {
                    Id = i,
                    Description = JobDescriptions[Random.Next(JobDescriptions.Length)],
                    PickupAddress = PickupAddresses[Random.Next(PickupAddresses.Length)],
                    DeliveryAddress = DeliveryAddresses[Random.Next(DeliveryAddresses.Length)],
                    CreatedDate = today.AddDays(-Random.Next(0, 7)), // Created in last week
                    ScheduledDate = scheduledDate,
                    ScheduledTime = scheduledTime, // Hour for dispatch board scheduling
                    CompletedDate = null,
                    TruckId = truck?.Id,
                    Truck = truck,
                    Status = isAssigned ? DispatchStatus.Assigned : DispatchStatus.Unassigned,
                    Priority = Random.Next(1, 6), // 1-5 priority
                    Notes = Random.Next(100) < 40 ? null : "Handle with care" // Some missing notes
                };

                jobs.Add(job);

                // Assign pickup/delivery time windows to ~60% of jobs for realistic display
                if (Random.Next(100) < 60)
                {
                    var pickupHour = Random.Next(6, 11); // 6–10 AM pickup start
                    job.PickupWindowStart        = scheduledDate.AddHours(pickupHour);
                    job.PickupWindowEnd          = job.PickupWindowStart.Value.AddHours(2);
                    job.DeliveryWindowStart      = job.PickupWindowEnd.Value.AddMinutes(Random.Next(30, 121));
                    job.DeliveryWindowEnd        = job.DeliveryWindowStart.Value.AddHours(Random.Next(2, 4));
                    job.EstimatedPickupMinutes   = Random.Next(15, 46);
                    job.EstimatedDeliveryMinutes = Random.Next(20, 61);
                }

                // Assign RequiredEquipment to ~50% of jobs using values that can match truck VehicleType
                if (Random.Next(100) < 50)
                    job.RequiredEquipment = RequiredEquipmentOptions[Random.Next(RequiredEquipmentOptions.Length)];

                // Seed load weight and unit count to ~70% of jobs for capacity display
                if (Random.Next(100) < 70)
                {
                    job.LoadWeightKg = Random.Next(50, 2001);  // 50 – 2 000 kg
                    job.LoadUnits    = Random.Next(1, 17);      // 1 – 16 pallets/units
                }

                // Create assignment if job is assigned
                if (isAssigned && truck != null)
                {
                    assignments.Add(new Assignment
                    {
                        Id = i,
                        JobId = job.Id,
                        Job = job,
                        TruckId = truck.Id,
                        Truck = truck,
                        AssignedDate = today.AddDays(-Random.Next(0, 3))
                    });
                }

                // Link company references — deterministic round-robin, no Random calls
                if (customerCompanies.Count > 0)
                {
                    var c = customerCompanies[(i - 1) % customerCompanies.Count];
                    job.CustomerId = c.Id;
                    job.Customer   = c;
                }
                if (shipperCompanies.Count > 0)
                {
                    var c = shipperCompanies[(i - 1) % shipperCompanies.Count];
                    job.ShipperId = c.Id;
                    job.Shipper   = c;
                }
                if (receiverCompanies.Count > 0)
                {
                    var c = receiverCompanies[(i - 1) % receiverCompanies.Count];
                    job.ReceiverId = c.Id;
                    job.Receiver   = c;
                }

                // Link pickup/delivery locations — round-robin; also overrides address strings
                // and syncs Shipper/Receiver to the location's company for display consistency.
                if (pickupLocations.Count > 0)
                {
                    var loc = pickupLocations[(i - 1) % pickupLocations.Count];
                    job.PickupLocationId = loc.Id;
                    job.PickupLocation   = loc;
                    if (!string.IsNullOrWhiteSpace(loc.Address))
                        job.PickupAddress = loc.Address;   // zone name preserved for EstimateTravelMinutes
                    job.ShipperId = loc.CompanyId;
                    job.Shipper   = loc.Company;
                }
                if (deliveryLocations.Count > 0)
                {
                    var loc = deliveryLocations[(i - 1) % deliveryLocations.Count];
                    job.DeliveryLocationId = loc.Id;
                    job.DeliveryLocation   = loc;
                    if (!string.IsNullOrWhiteSpace(loc.Address))
                        job.DeliveryAddress = loc.Address; // zone name preserved for EstimateTravelMinutes
                    job.ReceiverId = loc.CompanyId;
                    job.Receiver   = loc.Company;
                }
            }

            return (jobs, assignments);
        }

        /// <summary>
        /// Generates complete seed dataset: drivers, trucks, jobs, and assignments.
        /// </summary>
        public static (List<Driver> Drivers, List<Truck> Trucks, List<Job> Jobs, List<Assignment> Assignments, List<Company> Companies, List<CompanyLocation> Locations) GenerateAllData()
        {
            var drivers   = GenerateDrivers(30);
            var trucks    = GenerateTrucks(30, drivers);
            var companies = GenerateCompanies();
            var locations = GenerateLocations(companies);
            var (jobs, assignments) = GenerateJobs(100, trucks, companies, locations);
            return (drivers, trucks, jobs, assignments, companies, locations);
        }
    }
}
