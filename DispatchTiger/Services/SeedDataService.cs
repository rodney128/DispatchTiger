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

        private static readonly string[] PickupAddresses = new[]
        {
            "123 Main St, City A",
            "456 Oak Ave, City B",
            "789 Pine Rd, City C",
            "321 Elm St, City A",
            "654 Maple Dr, City B",
            "987 Birch Ln, City C",
            "111 Cedar Way, City A",
            "222 Spruce Ct, City B",
            null, // Some missing addresses
            "333 Willow St, City C",
            "444 Ash Rd, City A",
            "555 Poplar Ave, City B"
        };

        private static readonly string[] DeliveryAddresses = new[]
        {
            "9999 Delivery Pl, Warehouse Zone A",
            "8888 Distribution Dr, Warehouse Zone B",
            "7777 Logistics Ln, Warehouse Zone A",
            "6666 Fulfillment Way, Warehouse Zone C",
            "5555 Shipping St, Warehouse Zone B",
            "4444 Handling Rd, Warehouse Zone A",
            null, // Some missing addresses
            "3333 Processing Ave, Warehouse Zone C",
            "2222 Sorting Dr, Warehouse Zone B",
            "1111 Packing Ln, Warehouse Zone A"
        };

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

        private static readonly string[] VehicleTypes = new[]
        {
            "Van",
            "Truck",
            "Box Truck",
            "Flatbed",
            null, // Some missing vehicle types
            "Cargo Van"
        };

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
                    IsAvailable = Random.Next(100) > 20 // 80% available
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
