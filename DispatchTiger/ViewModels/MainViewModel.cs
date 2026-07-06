using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DispatchTiger.Models;
using DispatchTiger.Services;

namespace DispatchTiger.ViewModels
{
    /// <summary>
    /// Main view model for the dispatcher application.
    /// Manages UI state, data collections, selections, and the assignment workflow.
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private Job? _selectedJob;
        private Truck? _selectedTruck;
        private Truck? _stagedTruck;
        private string _currentView = "Day"; // "Day", "Month", or "Map"
        private string _statusMessage = "Ready";

        // One-level undo: stores the state needed to reverse the most recent manual assignment.
        private record UndoRecord(Job Job, Assignment AddedAssignment);
        private UndoRecord? _undoRecord;

        public ObservableCollection<Job>             UnassignedJobs    { get; }
        public ObservableCollection<Truck>            AvailableTrucks   { get; }
        public ObservableCollection<Job>              AllJobs           { get; }
        public ObservableCollection<Assignment>       Assignments       { get; }
        public ObservableCollection<Company>          Companies         { get; }
        public ObservableCollection<CompanyLocation>  CompanyLocations  { get; }

        public Job? SelectedJob
        {
            get => _selectedJob;
            set
            {
                _selectedJob = value;
                OnPropertyChanged();
                // Clear staged truck when the dispatcher moves to a new job
                StagedTruck = null;
            }
        }

        public Truck? SelectedTruck
        {
            get => _selectedTruck;
            set { _selectedTruck = value; OnPropertyChanged(); }
        }

        /// <summary>The truck staged for assignment to SelectedJob. Not yet assigned.</summary>
        public Truck? StagedTruck
        {
            get => _stagedTruck;
            set { _stagedTruck = value; OnPropertyChanged(); }
        }

        public string CurrentView
        {
            get => _currentView;
            set { _currentView = value; OnPropertyChanged(); }
        }

        /// <summary>Short status message shown in the footer after key dispatcher actions.</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        /// <summary>True when a manual assignment can be undone.</summary>
        public bool CanUndo => _undoRecord != null;

        public ICommand AssignJobCommand { get; }
        public ICommand UndoCommand { get; }

        /// <summary>Footer dispatch pulse counts, kept live via CollectionChanged.</summary>
        public int UnassignedCount => UnassignedJobs.Count;
        public int AssignedCount => AllJobs.Count(j => j.Status != DispatchStatus.Unassigned);
        public int TruckCount => AvailableTrucks.Count;

        public MainViewModel()
        {
            UnassignedJobs   = new ObservableCollection<Job>();
            AvailableTrucks  = new ObservableCollection<Truck>();
            AllJobs          = new ObservableCollection<Job>();
            Assignments      = new ObservableCollection<Assignment>();
            Companies        = new ObservableCollection<Company>();
            CompanyLocations = new ObservableCollection<CompanyLocation>();

            UnassignedJobs.CollectionChanged += (_, __) => { OnPropertyChanged(nameof(UnassignedCount)); OnPropertyChanged(nameof(AssignedCount)); };
            AllJobs.CollectionChanged += (_, __) => OnPropertyChanged(nameof(AssignedCount));
            AvailableTrucks.CollectionChanged += (_, __) => OnPropertyChanged(nameof(TruckCount));

            AssignJobCommand = new RelayCommand(AssignJob, CanAssignJob);
            UndoCommand = new RelayCommand(UndoAssignment, () => CanUndo);

            LoadSeedData();
        }

        /// <summary>
        /// Loads seed data from SeedDataService.
        /// </summary>
        private void LoadSeedData()
        {
            var (drivers, trucks, jobs, assignments, companies, locations) = SeedDataService.GenerateAllData();

            // Populate collections
            foreach (var company in companies)
                Companies.Add(company);

            foreach (var location in locations)
                CompanyLocations.Add(location);

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
        /// Result of a staged-assignment attempt, returned so views can surface
        /// view-specific confirmation banners without re-implementing the workflow.
        /// </summary>
        public readonly record struct AssignStagedResult(bool Success, string JobName, string TruckName);

        /// <summary>
        /// Centralized "assign the staged truck to the selected job" workflow shared by
        /// Map View and Day View. Validates state, promotes StagedTruck to SelectedTruck,
        /// invokes AssignJobCommand, sets the footer StatusMessage, and clears staging.
        /// Returns a result describing what happened so callers can show their own banner.
        /// </summary>
        /// <param name="reselectAfterAssign">
        /// When true (Map View), the just-assigned job and its truck are re-selected after
        /// assignment so the map keeps showing pickup/delivery/route and the assigned truck.
        /// When false (Day View / default), selection is left cleared as before.
        /// </param>
        public AssignStagedResult AssignStaged(bool reselectAfterAssign = false)
        {
            var job    = SelectedJob;
            var staged = StagedTruck;

            if (job == null || staged == null || job.Status != DispatchStatus.Unassigned)
                return new AssignStagedResult(false, string.Empty, string.Empty);

            // Capture display values before AssignJob() clears the selection.
            string jobName   = job.DisplayName;
            string truckName = staged.DisplayName;

            // Promote the staged truck to SelectedTruck only at the moment of assignment.
            SelectedTruck = staged;

            if (!AssignJobCommand.CanExecute(null))
            {
                StagedTruck = null;
                return new AssignStagedResult(false, jobName, truckName);
            }

            AssignJobCommand.Execute(null);

            // AssignJob() has cleared SelectedJob / SelectedTruck / StagedTruck.
            string ts = DateTime.Now.ToString("h:mm tt");
            StatusMessage = $"\u2713 {ts} \u00B7 Assigned {jobName} to {truckName} \u00B7 Undo available";

            // Belt-and-suspenders: guarantee staging is cleared even on an edge case.
            StagedTruck = null;

            // Map View opts into keeping the assigned-job context on the map: re-select the
            // now-assigned job and its truck so pickup/delivery/route/marker stay visible.
            // The job is now Assigned (not Unassigned), so the AssignmentPanel stays hidden
            // and StagedTruck remains null.
            if (reselectAfterAssign)
            {
                SelectedJob   = job;    // job.Status == Assigned, job.Truck == staged
                SelectedTruck = staged;
                StagedTruck   = null;   // SelectedJob setter may touch staging; keep it cleared
            }

            return new AssignStagedResult(true, jobName, truckName);
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

            // Store one-level undo record before clearing selection
            _undoRecord = new UndoRecord(SelectedJob, assignment);
            OnPropertyChanged(nameof(CanUndo));

            // Remove from unassigned list
            UnassignedJobs.Remove(SelectedJob);

            // Clear selection
            SelectedJob = null;
            SelectedTruck = null;
            StagedTruck = null;
        }

        /// <summary>
        /// Reverses the most recent manual assignment (one-level undo).
        /// </summary>
        private void UndoAssignment()
        {
            if (_undoRecord == null)
                return;

            var (job, addedAssignment) = _undoRecord;

            // Verify the job still exists in AllJobs and is still in Assigned state
            if (!AllJobs.Contains(job) || job.Status != DispatchStatus.Assigned)
            {
                StatusMessage = "↶ Undo not available — job state has changed.";
                _undoRecord = null;
                OnPropertyChanged(nameof(CanUndo));
                return;
            }

            // Reverse what AssignJob did
            job.Status = DispatchStatus.Unassigned;
            job.TruckId = null;
            job.Truck = null;

            Assignments.Remove(addedAssignment);
            UnassignedJobs.Add(job);

            StatusMessage = $"\u21B6 Undid assignment of {job.DisplayName}";

            // Clear the undo record — one-level only
            _undoRecord = null;
            OnPropertyChanged(nameof(CanUndo));
            StagedTruck = null;
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
