using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DispatchTiger.Models;
using DispatchTiger.ViewModels;

namespace DispatchTiger.Views
{
    public partial class DayView : UserControl
    {
        private DispatcherTimer _currentTimeTimer;

        public DayView()
        {
            InitializeComponent();
            Loaded += DayView_Loaded;
            Unloaded += DayView_Unloaded;
        }

        private void DayView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_currentTimeTimer != null)
            {
                _currentTimeTimer.Stop();
                _currentTimeTimer = null;
            }
        }

        private void DayView_Loaded(object sender, RoutedEventArgs e)
        {
            BuildScheduleGrid();
            StartCurrentTimeMarker();
        }

        /// <summary>
        /// Builds the hourly dispatch schedule grid.
        /// Expected layout: time labels in first column, then one column per truck.
        /// Rows are hourly from 12:00 AM to 11:00 PM (full 24-hour day).
        /// </summary>
        private void BuildScheduleGrid()
        {
            if (DataContext is not MainViewModel vm)
                return;

            var today = DateTime.Now.Date;
            var hours = vm.GetScheduledHours();
            var trucks = vm.AvailableTrucks;

            // Clear previous grid
            ScheduleGrid.Children.Clear();
            ScheduleGrid.RowDefinitions.Clear();
            ScheduleGrid.ColumnDefinitions.Clear();

            // Define columns: first column for time labels, then one per truck
            ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            foreach (var truck in trucks)
            {
                ScheduleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            }

            // Define rows: one row per hour
            foreach (var hour in hours)
            {
                ScheduleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
            }

            int rowIndex = 0;
            foreach (var hour in hours)
            {
                // Time label cell
                var timeLabel = new TextBlock
                {
                    Text = FormatHour(hour),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    FontSize = 11
                };
                Grid.SetRow(timeLabel, rowIndex);
                Grid.SetColumn(timeLabel, 0);
                ScheduleGrid.Children.Add(timeLabel);

                // Job cells for each truck column
                int colIndex = 1;
                foreach (var truck in trucks)
                {
                    var cellBorder = new Border
                    {
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 58)),
                        Background = new SolidColorBrush(Color.FromRgb(31, 31, 31)),
                        Margin = new Thickness(2)
                    };

                    // Get jobs for this truck/hour/day
                    var jobsForCell = vm.GetJobsByTruckAndHour(truck.Id, hour, today);

                    var cellStack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(2) };
                    foreach (var job in jobsForCell)
                    {
                        var jobTextBlock = new TextBlock
                        {
                            Text = job.Description,
                            FontSize = 9,
                            Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                            TextWrapping = TextWrapping.Wrap,
                            Cursor = System.Windows.Input.Cursors.Hand,
                            Tag = job
                        };
                        jobTextBlock.MouseLeftButtonUp += JobCell_MouseLeftButtonUp;
                        cellStack.Children.Add(jobTextBlock);
                    }

                    cellBorder.Child = cellStack;
                    Grid.SetRow(cellBorder, rowIndex);
                    Grid.SetColumn(cellBorder, colIndex);
                    ScheduleGrid.Children.Add(cellBorder);

                    colIndex++;
                }

                rowIndex++;
            }

            // Explicitly set ScheduleGrid dimensions so it renders at its full
            // fixed content size (24 hours × 60px = 1,440px) rather than being
            // stretched to the ScrollViewer viewport. This gives the ScrollViewer
            // content taller than its viewport so the center schedule scrolls.
            const int pixelsPerRow = 60;
            const int pixelsPerTruck = 120;
            const int timeColumnWidth = 60;
            int totalRows = hours.Count();

            ScheduleGrid.Height = totalRows * pixelsPerRow;
            ScheduleGrid.Width = timeColumnWidth + (trucks.Count * pixelsPerTruck);

            // Keep the current-time marker overlay the same size as the schedule
            CurrentTimeMarkerCanvas.Height = ScheduleGrid.Height;
            CurrentTimeMarkerCanvas.Width = ScheduleGrid.Width;
        }

        /// <summary>
        /// Formats an hour (0-23) as a readable time string (e.g., "6:00 AM").
        /// </summary>
        private string FormatHour(int hour)
        {
            var time = new DateTime(2000, 1, 1, hour, 0, 0);
            return time.ToString("h:00 tt");
        }

        private void JobCard_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is Job job && DataContext is MainViewModel vm)
            {
                vm.SelectedJob = job;
            }
        }

        private void JobCell_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.Tag is Job job && DataContext is MainViewModel vm)
            {
                vm.SelectedJob = job;
            }
        }

        private void TruckHeader_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is Truck truck && DataContext is MainViewModel vm)
            {
                vm.SelectedTruck = truck;
            }
        }

        private void StartCurrentTimeMarker()
        {
            if (CurrentTimeMarkerCanvas == null)
                return;

            _currentTimeTimer = new DispatcherTimer();
            _currentTimeTimer.Interval = TimeSpan.FromSeconds(30);
            _currentTimeTimer.Tick += (s, e) => UpdateCurrentTimeMarker();
            _currentTimeTimer.Start();

            UpdateCurrentTimeMarker();
        }

        private void UpdateCurrentTimeMarker()
        {
            if (CurrentTimeMarkerCanvas == null || ScheduleGrid == null)
                return;

            CurrentTimeMarkerCanvas.Children.Clear();

            var now = DateTime.Now;
            var totalMinutesInDay = now.Hour * 60 + now.Minute;
            var gridHeightInPixels = ScheduleGrid.ActualHeight;

            if (gridHeightInPixels <= 0)
                return;

            const int rowsInDay = 24;
            const int pixelsPerRow = 60;
            var totalGridHeight = rowsInDay * pixelsPerRow;

            var markerYPos = (totalMinutesInDay / (double)(rowsInDay * 60)) * totalGridHeight;

            var markerLine = new Line
            {
                X1 = 0,
                Y1 = markerYPos,
                X2 = CurrentTimeMarkerCanvas.ActualWidth,
                Y2 = markerYPos,
                Stroke = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                StrokeThickness = 2
            };

            var timeLabel = new TextBlock
            {
                Text = $"Now {now:h:mm tt}",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, Math.Max(0, markerYPos - 10), 0, 0)
            };

            Canvas.SetTop(timeLabel, markerYPos - 8);
            Canvas.SetLeft(timeLabel, 2);

            CurrentTimeMarkerCanvas.Children.Add(markerLine);
            CurrentTimeMarkerCanvas.Children.Add(timeLabel);
        }
    }
}
