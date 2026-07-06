using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DispatchTiger.Models;
using DispatchTiger.ViewModels;

namespace DispatchTiger.Views
{
    public partial class MonthView : UserControl
    {
        public MonthView()
        {
            InitializeComponent();
            this.Loaded += MonthView_Loaded;
            this.DataContextChanged += MonthView_DataContextChanged;
        }

        private MainViewModel? _vm;

        private void MonthView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
                _vm.PropertyChanged -= ViewModel_PropertyChanged;

            _vm = DataContext as MainViewModel;

            if (_vm != null)
            {
                _vm.PropertyChanged += ViewModel_PropertyChanged;
                BuildCalendar(_vm);
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Rebuild when selection or assignments change so today/selected highlights and
            // truck labels stay in sync with the rest of the app.
            if (_vm == null) return;
            if (e.PropertyName is nameof(MainViewModel.SelectedJob) or nameof(MainViewModel.Assignments))
                BuildCalendar(_vm);
        }

        private void MonthView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                BuildCalendar(vm);
            }
        }

        /// <summary>
        /// Builds a full month calendar grid showing all days and their jobs.
        /// </summary>
        private void BuildCalendar(MainViewModel vm)
        {
            var today = DateTime.Now;
            var firstDayOfMonth = new DateTime(today.Year, today.Month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            // Update header with month/year
            MonthHeader.Text = firstDayOfMonth.ToString("MMMM yyyy");

            // Clear previous calendar
            CalendarGrid.Children.Clear();
            CalendarGrid.ColumnDefinitions.Clear();
            CalendarGrid.RowDefinitions.Clear();

            // 7 columns for each day of week
            for (int col = 0; col < 7; col++)
            {
                CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            // Calculate rows needed (weeks in month + days from previous month to fill first week)
            int firstDayOfWeek = (int)firstDayOfMonth.DayOfWeek; // 0 = Sunday
            int totalCellsNeeded = firstDayOfWeek + lastDayOfMonth.Day;
            int weeksNeeded = (int)Math.Ceiling(totalCellsNeeded / 7.0);

            // Add rows for each week
            for (int row = 0; row < weeksNeeded; row++)
            {
                CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100) });
            }

            // Populate calendar cells
            int dayCounter = 1;
            int cellIndex = 0; // Start from first cell (including offset for first day of week)

            // Offset cells for days from previous month
            cellIndex = firstDayOfWeek;

            // Add day cells
            for (int day = 1; day <= lastDayOfMonth.Day; day++)
            {
                int row = cellIndex / 7;
                int col = cellIndex % 7;

                var jobDate = new DateTime(today.Year, today.Month, day);
                var dayCellBorder = CreateDayCell(jobDate, vm);

                Grid.SetRow(dayCellBorder, row);
                Grid.SetColumn(dayCellBorder, col);
                CalendarGrid.Children.Add(dayCellBorder);

                cellIndex++;
            }
        }

        /// <summary>
        /// Creates a single day cell with the day number and jobs for that date.
        /// </summary>
        private Border CreateDayCell(DateTime date, MainViewModel vm)
        {
            bool isToday = date.Date == DateTime.Now.Date;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(38, 38, 38)),
                BorderThickness = new Thickness(isToday ? 2 : 1),
                BorderBrush = isToday
                    ? new SolidColorBrush(Color.FromRgb(255, 140, 0))   // orange outline marks today
                    : new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                Margin = new Thickness(2),
                Padding = new Thickness(4)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Day number header
            var dayNumber = new TextBlock
            {
                Text = date.Day.ToString(),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = isToday
                    ? new SolidColorBrush(Color.FromRgb(255, 170, 60))  // brighter for today
                    : new SolidColorBrush(Color.FromRgb(200, 200, 200))
            };
            Grid.SetRow(dayNumber, 0);
            grid.Children.Add(dayNumber);

            // Jobs list for this day
            var jobsScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(jobsScrollViewer, 1);

            var jobsStackPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(2)
            };

            // Get all jobs scheduled for this date (both assigned and unassigned)
            var jobsForDate = vm.AllJobs.Where(j => j.ScheduledDate?.Date == date.Date).ToList();

            if (jobsForDate.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = "",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    Margin = new Thickness(2)
                };
                jobsStackPanel.Children.Add(emptyText);
            }
            else
            {
                foreach (var job in jobsForDate)
                {
                    var jobItemBorder = new Border
                    {
                        Background = job.Status == DispatchStatus.Unassigned
                            ? new SolidColorBrush(Color.FromRgb(50, 30, 20))  // Dark orange for unassigned
                            : new SolidColorBrush(Color.FromRgb(30, 50, 30)), // Dark green for assigned
                        BorderThickness = new Thickness(1),
                        BorderBrush = job.Status == DispatchStatus.Unassigned
                            ? new SolidColorBrush(Color.FromRgb(255, 140, 0))  // Orange border
                            : new SolidColorBrush(Color.FromRgb(100, 150, 100)), // Green border
                        Padding = new Thickness(3),
                        Margin = new Thickness(1),
                        CornerRadius = new CornerRadius(2),
                        Tag = job,
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    jobItemBorder.MouseLeftButtonUp += JobCell_MouseLeftButtonUp;

                    // Highlight the currently selected job so it stands out across the month grid.
                    if (vm.SelectedJob != null && ReferenceEquals(vm.SelectedJob, job))
                    {
                        jobItemBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 200, 255)); // cyan = selected
                        jobItemBorder.BorderThickness = new Thickness(2);
                    }

                    jobItemBorder.ToolTip = $"{job.DisplayName}\nStatus: {job.Status}\nTruck: {job.Truck?.PlateNumber ?? "Unassigned"}";

                    var jobGrid = new Grid();
                    jobGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    jobGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

                    var jobDesc = new TextBlock
                    {
                        Text = job.DisplayName,
                        FontSize = 8,
                        Foreground = job.Status == DispatchStatus.Unassigned
                            ? new SolidColorBrush(Color.FromRgb(255, 140, 0))   // orange = unassigned
                            : new SolidColorBrush(Color.FromRgb(120, 200, 120)), // green = assigned
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 120
                    };
                    Grid.SetColumn(jobDesc, 0);
                    jobGrid.Children.Add(jobDesc);

                    if (job.Status == DispatchStatus.Assigned && job.Truck != null)
                    {
                        var truckLabel = new TextBlock
                        {
                            Text = job.Truck.PlateNumber,
                            FontSize = 7,
                            Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100)),
                            Margin = new Thickness(3, 0, 0, 0)
                        };
                        Grid.SetColumn(truckLabel, 1);
                        jobGrid.Children.Add(truckLabel);
                    }
                    else if (job.Status == DispatchStatus.Unassigned)
                    {
                        var unassignedLabel = new TextBlock
                        {
                            Text = "[Unassigned]",
                            FontSize = 7,
                            Foreground = new SolidColorBrush(Color.FromRgb(200, 100, 0)),
                            Margin = new Thickness(3, 0, 0, 0)
                        };
                        Grid.SetColumn(unassignedLabel, 1);
                        jobGrid.Children.Add(unassignedLabel);
                    }

                    jobItemBorder.Child = jobGrid;
                    jobsStackPanel.Children.Add(jobItemBorder);
                }
            }

            jobsScrollViewer.Content = jobsStackPanel;
            grid.Children.Add(jobsScrollViewer);

            border.Child = grid;
            return border;
        }

        private void JobCell_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is Job job && DataContext is MainViewModel vm)
            {
                vm.SelectedJob = job;
            }
        }
    }
}
