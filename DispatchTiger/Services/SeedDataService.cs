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

            for (int i = 1; i <= count; i++)
            {
                // Some trucks have drivers, some don't
                int? driverId = Random.Next(100) < 70 ? drivers[Random.Next(drivers.Count)].Id : null;

                trucks.Add(new Truck
                {
                    Id = i,
                    PlateNumber = PlateNumbers[i - 1],
                    VehicleType = VehicleTypes[Random.Next(VehicleTypes.Length)],
                    DriverId = driverId,
                    Driver = driverId.HasValue ? drivers.First(d => d.Id == driverId) : null,
                    Capacity = Random.Next(100) < 30 ? null : Random.Next(500, 5000), // Some missing capacity
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
        public static (List<Job> Jobs, List<Assignment> Assignments) GenerateJobs(int count = 100, List<Truck>? trucks = null)
        {
            trucks ??= GenerateTrucks(30);
            var jobs = new List<Job>();
            var assignments = new List<Assignment>();
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
            }

            return (jobs, assignments);
        }

        /// <summary>
        /// Generates complete seed dataset: drivers, trucks, jobs, and assignments.
        /// </summary>
        public static (List<Driver> Drivers, List<Truck> Trucks, List<Job> Jobs, List<Assignment> Assignments) GenerateAllData()
        {
            var drivers = GenerateDrivers(30);
            var trucks = GenerateTrucks(30, drivers);
            var (jobs, assignments) = GenerateJobs(100, trucks);
            return (drivers, trucks, jobs, assignments);
        }
    }
}
