using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using DispatchTiger.Models;
using DispatchTiger.Services;

namespace DispatchTiger.ViewModels
{
    /// <summary>
    /// Main view model for the dispatcher application.
    /// Manages UI state, data collections, selections, and the assignment workflow.
    /// </summary>
    public class MainViewModel
    {
        private Job? _selectedJob;
        private Truck? _selectedTruck;
        private string _currentView = "Day"; // "Day" or "Month"

        public ObservableCollection<Job> UnassignedJobs { get; }
        public ObservableCollection<Truck> AvailableTrucks { get; }
        public ObservableCollection<Job> AllJobs { get; }
        public ObservableCollection<Assignment> Assignments { get; }

        public Job? SelectedJob
        {
            get => _selectedJob;
            set => _selectedJob = value;
        }

        public Truck? SelectedTruck
        {
            get => _selectedTruck;
            set => _selectedTruck = value;
        }

        public string CurrentView
        {
            get => _currentView;
            set => _currentView = value;
        }

        public ICommand AssignJobCommand { get; }

        public MainViewModel()
        {
            UnassignedJobs = new ObservableCollection<Job>();
            AvailableTrucks = new ObservableCollection<Truck>();
            AllJobs = new ObservableCollection<Job>();
            Assignments = new ObservableCollection<Assignment>();

            AssignJobCommand = new RelayCommand(AssignJob, CanAssignJob);

            LoadSeedData();
        }

        /// <summary>
        /// Loads seed data from SeedDataService.
        /// </summary>
        private void LoadSeedData()
        {
            var (drivers, trucks, jobs, assignments) = SeedDataService.GenerateAllData();

            // Populate collections
            foreach (var truck in trucks)
            {
                AvailableTrucks.Add(truck);
            }

            foreach (var job in jobs)
            {
                AllJobs.Add(job);
                if (job.Status == DispatchStatus.Unassigned)
                {
                    UnassignedJobs.Add(job);
                }
            }

            foreach (var assignment in assignments)
            {
                Assignments.Add(assignment);
            }
        }

        /// <summary>
        /// Determines if a job can be assigned.
        /// </summary>
        private bool CanAssignJob()
        {
            return SelectedJob != null && SelectedTruck != null && SelectedJob.Status == DispatchStatus.Unassigned;
        }

        /// <summary>
        /// Assigns the selected job to the selected truck.
        /// </summary>
        private void AssignJob()
        {
            if (SelectedJob == null || SelectedTruck == null)
                return;

            if (SelectedJob.Status != DispatchStatus.Unassigned)
                return;

            // Update job
            SelectedJob.TruckId = SelectedTruck.Id;
            SelectedJob.Truck = SelectedTruck;
            SelectedJob.Status = DispatchStatus.Assigned;

            // Create assignment record
            var assignment = new Assignment
            {
                Id = Assignments.Count + 1,
                JobId = SelectedJob.Id,
                Job = SelectedJob,
                TruckId = SelectedTruck.Id,
                Truck = SelectedTruck,
                AssignedDate = DateTime.Now
            };

            Assignments.Add(assignment);

            // Remove from unassigned list
            UnassignedJobs.Remove(SelectedJob);

            // Clear selection
            SelectedJob = null;
            SelectedTruck = null;
        }

        /// <summary>
        /// Gets jobs assigned to a specific truck.
        /// </summary>
        public IEnumerable<Job> GetJobsForTruck(int truckId)
        {
            return AllJobs.Where(j => j.TruckId == truckId && j.Status == DispatchStatus.Assigned);
        }

        /// <summary>
        /// Gets jobs assigned to a truck by day.
        /// </summary>
        public IEnumerable<IGrouping<int, Job>> GetJobsByTruckAndDay(int truckId)
        {
            return GetJobsForTruck(truckId)
                .GroupBy(j => j.ScheduledDate?.Day ?? 0)
                .OrderBy(g => g.Key);
        }

        /// <summary>
        /// Gets jobs for a truck at a specific hour on a specific day.
        /// Used for dispatch board grid population.
        /// </summary>
        public IEnumerable<Job> GetJobsByTruckAndHour(int truckId, int hour, DateTime date)
        {
            return AllJobs.Where(j =>
                j.TruckId == truckId &&
                j.Status == DispatchStatus.Assigned &&
                j.ScheduledDate?.Date == date.Date &&
                j.ScheduledTime == hour);
        }

        /// <summary>
        /// Gets all unique hours that have jobs for dispatch board.
        /// Returns hours from 0 (12:00 AM) to 23 (11:00 PM) for a full 24-hour day.
        /// </summary>
        public IEnumerable<int> GetScheduledHours()
        {
            return Enumerable.Range(0, 24); // 12:00 AM to 11:00 PM (24 hours)
        }

        /// <summary>
        /// Gets all trucks with assigned jobs for today.
        /// </summary>
        public IEnumerable<Truck> GetTrucksWithAssignmentsForDay(DateTime date)
        {
            var trucksWithJobs = AllJobs
                .Where(j => j.TruckId.HasValue &&
                           j.Status == DispatchStatus.Assigned &&
                           j.ScheduledDate?.Date == date.Date)
                .Select(j => j.TruckId)
                .Distinct();

            return AvailableTrucks.Where(t => trucksWithJobs.Contains(t.Id));
        }
    }
}
