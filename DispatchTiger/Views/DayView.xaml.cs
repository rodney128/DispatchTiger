using System;
using System.ComponentModel;
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

        // Tracks the truck the dispatcher has staged from the candidate panel (not yet assigned)
        private Truck? _selectedCandidateTruck;

        // When false (default), Unknown and Blocked rows are hidden from the candidate table
        private bool _showAllCandidateTrucks;

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
            if (DataContext is MainViewModel vm)
                vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private void DayView_Loaded(object sender, RoutedEventArgs e)
        {
            BuildScheduleGrid();
            StartCurrentTimeMarker();
            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += OnViewModelPropertyChanged;
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

            // Build truck header row to match schedule columns exactly
            var truckHeaderGrid = FindName("TruckHeaderGrid") as Grid;
            if (truckHeaderGrid != null)
            {
                truckHeaderGrid.Children.Clear();
                truckHeaderGrid.ColumnDefinitions.Clear();
                // Time label spacer column
                truckHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                int truckColIndex = 1;
                foreach (var truck in trucks)
                {
                    truckHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                    var headerBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(43, 43, 43)),
                        BorderThickness = new Thickness(1, 0, 0, 0),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 58)),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Tag = truck
                    };
                    headerBorder.MouseLeftButtonUp += TruckHeader_MouseLeftButtonUp;

                    var headerStack = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(4, 3, 4, 3)
                    };

                    // Plate number — primary identifier
                    headerStack.Children.Add(new TextBlock
                    {
                        Text = truck.PlateNumber,
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(242, 140, 40)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });

                    // Driver name — if linked
                    if (truck.Driver != null)
                    {
                        headerStack.Children.Add(new TextBlock
                        {
                            Text = truck.Driver.Name,
                            FontSize = 10,
                            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });
                    }

                    // Vehicle type — if available
                    if (!string.IsNullOrEmpty(truck.VehicleType))
                    {
                        headerStack.Children.Add(new TextBlock
                        {
                            Text = truck.VehicleType,
                            FontSize = 10,
                            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });
                    }

                    // Unavailable trucks: show indicator only; omit Free/Loc (not actionable in header)
                    if (!truck.IsAvailable)
                    {
                        headerStack.Children.Add(new TextBlock
                        {
                            Text = "\u25CF Unavailable",
                            FontSize = 10,
                            Foreground = new SolidColorBrush(Color.FromRgb(255, 92, 53)),
                            HorizontalAlignment = HorizontalAlignment.Center
                        });
                    }
                    else
                    {
                        // AvailableAt and Location only shown for available trucks
                        if (truck.AvailableAt.HasValue)
                        {
                            headerStack.Children.Add(new TextBlock
                            {
                                Text = $"Free {truck.AvailableAt.Value:h:mm tt}",
                                FontSize = 10,
                                Foreground = new SolidColorBrush(Color.FromRgb(140, 200, 140)),
                                HorizontalAlignment = HorizontalAlignment.Center
                            });
                        }

                        if (!string.IsNullOrEmpty(truck.CurrentLocation))
                        {
                            headerStack.Children.Add(new TextBlock
                            {
                                Text = $"Loc: {truck.CurrentLocation}",
                                FontSize = 10,
                                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 180)),
                                HorizontalAlignment = HorizontalAlignment.Center,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            });
                        }
                    }

                    headerBorder.Child = headerStack;
                    Grid.SetColumn(headerBorder, truckColIndex);
                    truckHeaderGrid.Children.Add(headerBorder);
                    truckColIndex++;
                }
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
                        bool isAssigned = job.Status == DispatchStatus.Assigned;

                        // Colors: assigned = dark green theme, unassigned = dark orange theme
                        var cardBackground = isAssigned
                            ? new SolidColorBrush(Color.FromRgb(28, 48, 28))
                            : new SolidColorBrush(Color.FromRgb(50, 28, 10));
                        var accentColor = isAssigned
                            ? Color.FromRgb(100, 170, 100)
                            : Color.FromRgb(255, 140, 0);
                        var accentBrush = new SolidColorBrush(accentColor);
                        var mutedBrush = new SolidColorBrush(Color.FromRgb(170, 170, 170));

                        // Outer card border
                        var cardBorder = new Border
                        {
                            Background = cardBackground,
                            BorderBrush = accentBrush,
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(4),
                            Margin = new Thickness(0, 0, 0, 3),
                            Cursor = System.Windows.Input.Cursors.Hand,
                            Tag = job,
                            ToolTip = $"{job.Description}\nAddress: {job.DeliveryAddress ?? "—"}\nPriority: {job.Priority?.ToString() ?? "—"}\nTruck: {job.Truck?.PlateNumber ?? "Unassigned"}\nStatus: {job.Status}"
                        };
                        cardBorder.MouseLeftButtonUp += JobCard_MouseLeftButtonUp;

                        // Inner layout: left accent strip + content
                        var innerGrid = new Grid();
                        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        // Left accent strip
                        var accentStrip = new Border
                        {
                            Background = accentBrush,
                            CornerRadius = new CornerRadius(3, 0, 0, 3)
                        };
                        Grid.SetColumn(accentStrip, 0);
                        innerGrid.Children.Add(accentStrip);

                        // Content stack
                        var contentStack = new StackPanel
                        {
                            Orientation = Orientation.Vertical,
                            Margin = new Thickness(5, 4, 4, 4)
                        };
                        Grid.SetColumn(contentStack, 1);

                        // Title: job description (bold)
                        contentStack.Children.Add(new TextBlock
                        {
                            Text = job.Description,
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                            TextTrimming = TextTrimming.CharacterEllipsis
                        });

                        // Priority + truck row
                        var line2 = new TextBlock
                        {
                            FontSize = 10,
                            Foreground = accentBrush,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        };
                        var priorityText = job.Priority.HasValue ? $"P{job.Priority}" : "P—";
                        var truckText = job.Truck?.PlateNumber ?? "Unassigned";
                        line2.Text = $"{priorityText}  ·  {truckText}";
                        contentStack.Children.Add(line2);

                        // Delivery address row
                        if (!string.IsNullOrWhiteSpace(job.DeliveryAddress))
                        {
                            contentStack.Children.Add(new TextBlock
                            {
                                Text = job.DeliveryAddress,
                                FontSize = 10,
                                Foreground = mutedBrush,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            });
                        }

                        innerGrid.Children.Add(contentStack);
                        cardBorder.Child = innerGrid;
                        cellStack.Children.Add(cardBorder);
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

        // ── Candidate panel ─────────────────────────────────────────────────────

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedJob))
            {
                _selectedCandidateTruck = null;   // reset staged truck on new job selection
                _showAllCandidateTrucks = false;  // collapse to usable-only view for fresh job
                BuildCandidatePanel();
            }
        }

        /// <summary>
        /// Rebuilds the truck candidate comparison panel whenever SelectedJob changes.
        /// Shows basic Candidate / Blocked fit labels using existing Truck fields only.
        /// </summary>
        private void BuildCandidatePanel()
        {
            if (DataContext is not MainViewModel vm) return;

            var job = vm.SelectedJob;
            CandidatePanelBorder.Visibility = job == null ? Visibility.Collapsed : Visibility.Visible;

            if (job == null) return;

            // -- Job summary line --
            CandidateJobSummaryPanel.Children.Clear();

            AddSummaryRun(CandidateJobSummaryPanel, "Truck candidates for: ", Color.FromRgb(136, 136, 136));
            AddSummaryRun(CandidateJobSummaryPanel, job.Description, Color.FromRgb(242, 140, 40), bold: true);

            if (!string.IsNullOrEmpty(job.PickupAddress))
            {
                AddSummaryRun(CandidateJobSummaryPanel, "  ·  Pickup: ", Color.FromRgb(80, 80, 80));
                var pickupText = job.PickupAddress;
                if (job.PickupWindowStart.HasValue && job.PickupWindowEnd.HasValue)
                    pickupText += $" ({job.PickupWindowStart.Value:h:mm tt}–{job.PickupWindowEnd.Value:h:mm tt})";
                else if (job.PickupWindowStart.HasValue)
                    pickupText += $" (from {job.PickupWindowStart.Value:h:mm tt})";
                AddSummaryRun(CandidateJobSummaryPanel, pickupText, Color.FromRgb(187, 187, 187));
            }
            if (!string.IsNullOrEmpty(job.DeliveryAddress))
            {
                AddSummaryRun(CandidateJobSummaryPanel, "  ·  Delivery: ", Color.FromRgb(80, 80, 80));
                var deliveryText = job.DeliveryAddress;
                if (job.DeliveryWindowStart.HasValue && job.DeliveryWindowEnd.HasValue)
                    deliveryText += $" ({job.DeliveryWindowStart.Value:h:mm tt}–{job.DeliveryWindowEnd.Value:h:mm tt})";
                else if (job.DeliveryWindowStart.HasValue)
                    deliveryText += $" (from {job.DeliveryWindowStart.Value:h:mm tt})";
                AddSummaryRun(CandidateJobSummaryPanel, deliveryText, Color.FromRgb(187, 187, 187));
            }
            if (job.ScheduledDate.HasValue)
            {
                var sched = job.ScheduledDate.Value.ToString("MM/dd");
                if (job.ScheduledTime.HasValue)
                    sched += $" @{job.ScheduledTime.Value:D2}:00";
                AddSummaryRun(CandidateJobSummaryPanel, "  ·  Sched: ", Color.FromRgb(80, 80, 80));
                AddSummaryRun(CandidateJobSummaryPanel, sched, Color.FromRgb(187, 187, 187));
            }

            // Selection status — appended to the summary line
            if (_selectedCandidateTruck != null)
            {
                AddSummaryRun(CandidateJobSummaryPanel, "    ✔ Staged: ", Color.FromRgb(100, 200, 100));
                AddSummaryRun(CandidateJobSummaryPanel, _selectedCandidateTruck.PlateNumber,
                    Color.FromRgb(242, 200, 80), bold: true);
            }

            // -- Action bar: Assign staged truck button --
            CandidateActionBar.Children.Clear();

            bool canAssign = _selectedCandidateTruck != null && job.Status == DispatchStatus.Unassigned;

            if (canAssign)
            {
                var assignBtn = new Button
                {
                    Content    = $"✓ Assign {_selectedCandidateTruck!.PlateNumber} to Job {job.Id}",
                    FontSize   = 11,
                    Padding    = new Thickness(8, 2, 8, 2),
                    Margin     = new Thickness(0, 0, 8, 0),
                    Cursor     = System.Windows.Input.Cursors.Hand,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    BorderThickness = new Thickness(0)
                };

                var stagedTruck = _selectedCandidateTruck;   // capture for closure
                assignBtn.Click += (_, _) =>
                {
                    if (DataContext is not MainViewModel assignVm) return;
                    if (assignVm.SelectedJob == null) return;
                    if (stagedTruck == null) return;
                    if (assignVm.SelectedJob.Status != DispatchStatus.Unassigned) return;

                    // Capture display values before Execute clears SelectedJob
                    int    capturedJobId   = assignVm.SelectedJob.Id;
                    string capturedDesc    = assignVm.SelectedJob.Description;
                    string capturedPlate   = stagedTruck.PlateNumber;

                    // Set vm.SelectedTruck only at the moment of assignment, not during staging
                    assignVm.SelectedTruck = stagedTruck;

                    if (assignVm.AssignJobCommand.CanExecute(null))
                    {
                        assignVm.AssignJobCommand.Execute(null);
                        // Only update status after a confirmed successful execute
                        string ts = DateTime.Now.ToString("h:mm tt");
                        assignVm.StatusMessage = $"✓ {ts} · Assigned \"{capturedDesc}\" to {capturedPlate}";
                    }

                    // Clear local staging — AssignJob sets SelectedJob = null which collapses the panel
                    _selectedCandidateTruck = null;
                };

                CandidateActionBar.Children.Add(assignBtn);
            }

            // -- Candidate rows --
            CandidateRowsPanel.Children.Clear();

            var orange    = Color.FromRgb(242, 140,  40);
            var green     = Color.FromRgb( 76, 175,  80);
            var red       = Color.FromRgb(255,  92,  53);
            var lightGray = Color.FromRgb(190, 190, 190);
            var mutedGray = Color.FromRgb(140, 140, 140);

            // Phase 1: evaluate all trucks before touching the UI
            var candidates = vm.AvailableTrucks
                .Select(truck =>
                {
                    bool avail = truck.IsAvailable;

                    string statusText  = avail ? "● Free" : "● Busy";
                    Color  statusColor = avail ? green : red;

                    string reason;
                    if (!avail)
                        reason = "Truck unavailable";
                    else if (truck.Driver != null)
                        reason = "Available · Driver assigned";
                    else
                        reason = "Available · No driver";

                    if (avail && !string.IsNullOrEmpty(truck.VehicleType))
                        reason += $" · {truck.VehicleType}";

                    if (avail && truck.AvailableAt.HasValue)
                        reason += $" · Free {truck.AvailableAt.Value:h:mm tt}";

                    if (avail && !string.IsNullOrEmpty(truck.CurrentLocation))
                        reason += $" · {truck.CurrentLocation}";

                    var (timeFitLabel,  timeFitColor,  timeFitDetail)  = GetTimeFit(job, truck);
                    var (routeFitLabel, routeFitColor, routeFitDetail) = GetRouteFit(job, truck);
                    var (equipLabel,    equipColor,    equipReason)    = GetEquipmentFit(job, truck);
                    var (fitLabel,      fitColor)                      = GetOverallFit(truck, timeFitLabel, routeFitLabel, equipLabel);

                    if (avail)
                    {
                        reason += timeFitDetail is not null
                            ? $" · {timeFitLabel}; {timeFitDetail}"
                            : $" · {timeFitLabel}";

                        if (routeFitDetail is not null)
                            reason += $"; {routeFitDetail}";

                        if (equipReason is not null)
                            reason += $"; {equipReason}";
                    }

                    string shortReason = BuildShortReason(
                        avail, fitLabel, timeFitLabel, timeFitDetail,
                        routeFitLabel, routeFitDetail, equipLabel);

                    return new
                    {
                        Truck          = truck,
                        Avail          = avail,
                        StatusText     = statusText,
                        StatusColor    = statusColor,
                        Reason         = reason,
                        ShortReason    = shortReason,
                        TimeFitLabel   = timeFitLabel,
                        TimeFitColor   = timeFitColor,
                        RouteFitLabel  = routeFitLabel,
                        RouteFitColor  = routeFitColor,
                        EquipLabel     = equipLabel,
                        EquipColor     = equipColor,
                        FitLabel       = fitLabel,
                        FitColor       = fitColor
                    };
                })
                // Phase 2: sort — best fit first, then earliest AvailableAt, then plate
                .OrderBy(c => GetOverallFitSortRank(c.FitLabel))
                .ThenBy(c  => c.Truck.AvailableAt ?? DateTime.MaxValue)
                .ThenBy(c  => c.Truck.PlateNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Phase 3: render sorted rows
            // Identify the first usable candidate (Best/Good/Risky) to mark as Recommended
            var recommendedCandidate = candidates
                .FirstOrDefault(c => c.FitLabel is "Best" or "Good" or "Risky");
            var recommendedTruck = recommendedCandidate?.Truck;

            // -- Recommendation summary bar --
            CandidateRecommendationContent.Children.Clear();

            void AddBarRun(string text, Color color, bool bold = false)
            {
                CandidateRecommendationContent.Children.Add(new TextBlock
                {
                    Text       = text,
                    FontSize   = 11,
                    FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = new SolidColorBrush(color),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            var dimGray  = Color.FromRgb( 90,  90,  90);
            var softGray = Color.FromRgb(160, 160, 160);
            var gold     = Color.FromRgb(242, 200,  80);

            if (recommendedCandidate != null)
            {
                AddBarRun("★ Recommended: ", dimGray);
                AddBarRun(recommendedTruck!.PlateNumber, gold, bold: true);
                AddBarRun("  —  ", dimGray);
                AddBarRun(recommendedCandidate.FitLabel, new SolidColorBrush(recommendedCandidate.FitColor).Color, bold: true);

                // Compact why-line: pick available detail tokens
                var whyParts = new System.Collections.Generic.List<string>();
                if (recommendedCandidate.TimeFitLabel == "Good")
                    whyParts.Add("timing OK");
                else if (!string.IsNullOrEmpty(recommendedCandidate.TimeFitLabel))
                    whyParts.Add(recommendedCandidate.TimeFitLabel.ToLowerInvariant());

                if (recommendedCandidate.RouteFitLabel == "Efficient")
                    whyParts.Add("efficient route");
                else if (recommendedCandidate.RouteFitLabel == "Acceptable")
                    whyParts.Add("acceptable route");
                else if (!string.IsNullOrEmpty(recommendedCandidate.RouteFitLabel))
                    whyParts.Add(recommendedCandidate.RouteFitLabel.ToLowerInvariant());

                if (recommendedCandidate.EquipLabel == "Match")
                    whyParts.Add("equipment match");
                else if (recommendedCandidate.EquipLabel == "Not specified")
                    whyParts.Add("no equip. req.");

                if (whyParts.Count > 0)
                {
                    AddBarRun("    Why: ", dimGray);
                    AddBarRun(string.Join(" · ", whyParts), softGray);
                }

                if (_selectedCandidateTruck != null)
                {
                    AddBarRun("    Staged: ", dimGray);
                    AddBarRun(_selectedCandidateTruck.PlateNumber, gold, bold: true);
                }

                // Stage Recommended button — inline in the recommendation bar
                bool recAlreadyStaged = _selectedCandidateTruck != null
                    && ReferenceEquals(_selectedCandidateTruck, recommendedTruck);

                var stageRecBtn = new Button
                {
                    FontSize        = 10,
                    Padding         = new Thickness(6, 1, 6, 1),
                    Margin          = new Thickness(12, 0, 0, 0),
                    Cursor          = recAlreadyStaged
                        ? System.Windows.Input.Cursors.Arrow
                        : System.Windows.Input.Cursors.Hand,
                    BorderThickness = new Thickness(1)
                };

                if (recAlreadyStaged)
                {
                    stageRecBtn.Content         = "\u2714 Recommended staged";
                    stageRecBtn.Foreground      = new SolidColorBrush(Color.FromRgb(100, 180, 100));
                    stageRecBtn.Background      = Brushes.Transparent;
                    stageRecBtn.BorderBrush     = new SolidColorBrush(Color.FromRgb(60, 100, 60));
                    stageRecBtn.IsEnabled       = false;
                }
                else
                {
                    stageRecBtn.Content         = "\u2605 Stage recommended";
                    stageRecBtn.Foreground      = new SolidColorBrush(Color.FromRgb(242, 200, 80));
                    stageRecBtn.Background      = Brushes.Transparent;
                    stageRecBtn.BorderBrush     = new SolidColorBrush(Color.FromRgb(150, 120, 40));

                    var truckToStage = recommendedTruck!;   // capture for closure
                    stageRecBtn.Click += (_, _) =>
                    {
                        _selectedCandidateTruck = truckToStage;
                        BuildCandidatePanel();
                    };
                }

                CandidateRecommendationContent.Children.Add(stageRecBtn);

                CandidateRecommendationBar.Visibility = Visibility.Visible;
            }
            else
            {
                AddBarRun("★ Recommended: ", dimGray);
                AddBarRun("None", softGray);
                AddBarRun("  —  no usable truck or insufficient data", dimGray);
                CandidateRecommendationBar.Visibility = Visibility.Visible;
            }

            // -- Filter toggle: count hidden rows and add toggle button to action bar --
            int hiddenCount = candidates.Count(c => c.FitLabel is "Unknown" or "Blocked");

            if (hiddenCount > 0)
            {
                string toggleText = _showAllCandidateTrucks
                    ? "Hide unavailable trucks"
                    : $"Show all trucks ({hiddenCount} hidden)";

                var toggleBtn = new Button
                {
                    Content         = toggleText,
                    FontSize        = 10,
                    Padding         = new Thickness(6, 1, 6, 1),
                    Margin          = new Thickness(0, 0, 0, 0),
                    Cursor          = System.Windows.Input.Cursors.Hand,
                    Foreground      = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                    Background      = Brushes.Transparent,
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(70, 70, 70)),
                    BorderThickness = new Thickness(1)
                };

                toggleBtn.Click += (_, _) =>
                {
                    _showAllCandidateTrucks = !_showAllCandidateTrucks;

                    // If collapsing back to filtered view, clear any staged truck that is now hidden
                    if (!_showAllCandidateTrucks && _selectedCandidateTruck != null)
                    {
                        var stagedFit = candidates
                            .FirstOrDefault(c => ReferenceEquals(c.Truck, _selectedCandidateTruck))
                            ?.FitLabel;
                        if (stagedFit is "Unknown" or "Blocked")
                            _selectedCandidateTruck = null;
                    }

                    BuildCandidatePanel();
                };

                CandidateActionBar.Children.Add(toggleBtn);
            }

            // Apply display filter: hide Unknown/Blocked rows unless the dispatcher toggled them on
            var visibleCandidates = _showAllCandidateTrucks
                ? candidates
                : candidates.Where(c => c.FitLabel is not ("Unknown" or "Blocked")).ToList();

            foreach (var c in visibleCandidates)
            {
                var truck = c.Truck;
                bool isRecommended = recommendedTruck != null && ReferenceEquals(truck, recommendedTruck);
                bool isSelected    = _selectedCandidateTruck != null && ReferenceEquals(truck, _selectedCandidateTruck);
                bool isBlocked     = c.FitLabel == "Blocked";

                // Prepend the star marker only to the single recommended row
                string displayReason = isRecommended
                    ? $"\u2605 {c.ShortReason}"
                    : c.ShortReason;
                Color reasonColor = isRecommended
                    ? Color.FromRgb(242, 200, 80)   // warm gold to stand out
                    : mutedGray;

                // Row background: selected > recommended > default
                var rowBackground = isSelected
                    ? new SolidColorBrush(Color.FromRgb(30, 60, 40))    // dark green tint
                    : isBlocked
                        ? new SolidColorBrush(Color.FromRgb(30, 20, 20)) // dark red tint for blocked
                        : new SolidColorBrush(Color.FromRgb(28, 28, 28)); // default dark

                var row = new Grid
                {
                    Margin     = new Thickness(0, 0, 0, 1),
                    Background = rowBackground,
                    Cursor     = isBlocked
                        ? System.Windows.Input.Cursors.No
                        : System.Windows.Input.Cursors.Hand,
                    Tag = truck
                };

                if (!isBlocked)
                {
                    row.MouseLeftButtonUp += (_, _) =>
                    {
                        _selectedCandidateTruck = truck;
                        BuildCandidatePanel();   // redraw to reflect selection
                    };
                }
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });   // Truck
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });  // Driver
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });   // Type
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });   // Status
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });   // Equipment
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });  // Time Fit
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });   // Route Fit
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });   // Fit
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Reason

                void AddCell(int col, string text, Color color, bool bold = false)
                {
                    var tb = new TextBlock
                    {
                        Text = text,
                        FontSize = 11,
                        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = new SolidColorBrush(color),
                        VerticalAlignment = VerticalAlignment.Center,
                        Padding = new Thickness(4, 3, 4, 3),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Grid.SetColumn(tb, col);
                    row.Children.Add(tb);
                }

                AddCell(0, truck.PlateNumber,                        orange,          bold: true);
                AddCell(1, truck.Driver?.Name ?? "—",               truck.Driver != null ? lightGray : mutedGray);
                AddCell(2, truck.VehicleType  ?? "—",               !string.IsNullOrEmpty(truck.VehicleType) ? lightGray : mutedGray);
                AddCell(3, c.StatusText,                             c.StatusColor);
                AddCell(4, ShortenLabel(c.EquipLabel),               c.EquipColor);
                AddCell(5, ShortenLabel(c.TimeFitLabel),             c.TimeFitColor);
                AddCell(6, ShortenLabel(c.RouteFitLabel),            c.RouteFitColor);
                AddCell(7, c.FitLabel,                               c.FitColor,   bold: true);
                AddCell(8, displayReason,                            reasonColor);

                CandidateRowsPanel.Children.Add(row);
            }
        }

        private static void AddSummaryRun(StackPanel panel, string text, Color color, bool bold = false)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        /// <summary>
        /// Derives a compact 1–2 fact display string for the candidate row Reason column.
        /// Uses already-computed fit labels and detail strings; no scoring recalculation.
        /// Priority: equipment mismatch > unavailable > time problem > slack/route detail.
        /// </summary>
        private static string BuildShortReason(
            bool avail,
            string fitLabel,
            string timeFitLabel,
            string? timeFitDetail,
            string routeFitLabel,
            string? routeFitDetail,
            string equipLabel)
        {
            // 1. Equipment mismatch blocks the truck entirely
            if (equipLabel == "Mismatch")
                return "equipment mismatch";

            // 2. Truck is physically unavailable
            if (!avail)
                return "truck unavailable";

            // 3. Time Fit is a hard problem (late or missing data)
            if (timeFitLabel.StartsWith("Late ", StringComparison.Ordinal))
                return timeFitLabel.ToLowerInvariant();
            if (timeFitLabel.StartsWith("Missing", StringComparison.Ordinal) ||
                timeFitLabel.StartsWith("Route estimate", StringComparison.Ordinal))
                return ShortenLabel(timeFitLabel).ToLowerInvariant();

            // 4. Time Fit is tight
            if (timeFitLabel is "Tight pickup" or "Tight delivery")
            {
                // Extract just the relevant slack from the detail string if available
                if (timeFitDetail is not null)
                {
                    // detail is "pickup slack Xm; delivery slack Ym" — show the tight one
                    string tightPart = timeFitLabel == "Tight pickup"
                        ? ExtractSlackToken(timeFitDetail, "pickup slack")
                        : ExtractSlackToken(timeFitDetail, "delivery slack");
                    return string.IsNullOrEmpty(tightPart)
                        ? timeFitLabel.ToLowerInvariant()
                        : $"{timeFitLabel.ToLowerInvariant()} · {tightPart}";
                }
                return timeFitLabel.ToLowerInvariant();
            }

            // 5. Time Fit is Good — show slack tokens if available
            if (timeFitLabel == "Good" && timeFitDetail is not null)
            {
                // Extract compact "pickup slack X · delivery slack Y"
                string pickupPart   = ExtractSlackToken(timeFitDetail, "pickup slack");
                string deliveryPart = ExtractSlackToken(timeFitDetail, "delivery slack");

                var slackParts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(pickupPart))   slackParts.Add($"pickup {pickupPart}");
                if (!string.IsNullOrEmpty(deliveryPart)) slackParts.Add($"delivery {deliveryPart}");

                string slackText = string.Join(" · ", slackParts);

                // Append compact route detail if available and short
                if (routeFitDetail is not null)
                {
                    string emptyToken = ExtractEmptyPct(routeFitDetail);
                    if (!string.IsNullOrEmpty(emptyToken))
                        slackText = string.IsNullOrEmpty(slackText)
                            ? emptyToken
                            : $"{slackText} · {emptyToken}";
                }

                return string.IsNullOrEmpty(slackText) ? "timing OK" : slackText;
            }

            // 6. Route-only issues (Good time but route problem)
            if (routeFitLabel == "High deadhead" && routeFitDetail is not null)
            {
                string emptyToken = ExtractEmptyPct(routeFitDetail);
                return string.IsNullOrEmpty(emptyToken) ? "high deadhead" : $"high deadhead · {emptyToken}";
            }

            // 7. Missing route data
            if (routeFitLabel.StartsWith("Missing", StringComparison.Ordinal) ||
                routeFitLabel == "Route estimate missing")
                return "no route estimate";

            // 8. Generic fallback
            return fitLabel.ToLowerInvariant();
        }

        /// <summary>Extracts a slack value token (e.g. "45 min" or "1h 20m") for a named key from a detail string.</summary>
        private static string ExtractSlackToken(string detail, string key)
        {
            // detail format: "pickup slack 45 min; delivery slack 1h 20m"
            int idx = detail.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return string.Empty;
            int valueStart = idx + key.Length;
            if (valueStart >= detail.Length) return string.Empty;
            // skip leading space
            while (valueStart < detail.Length && detail[valueStart] == ' ') valueStart++;
            // read until ';' or end
            int end = detail.IndexOf(';', valueStart);
            string token = end < 0
                ? detail[valueStart..].Trim()
                : detail[valueStart..end].Trim();
            return token;
        }

        /// <summary>Extracts the "empty N%" token from a route detail string.</summary>
        private static string ExtractEmptyPct(string detail)
        {
            // detail format: "deadhead X min; loaded Y min; empty N%"
            const string key = "empty ";
            int idx = detail.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return string.Empty;
            int valueStart = idx + key.Length;
            int end = detail.IndexOf(';', valueStart);
            string pct = end < 0
                ? detail[valueStart..].Trim()
                : detail[valueStart..end].Trim();
            return string.IsNullOrEmpty(pct) ? string.Empty : $"empty {pct}";
        }

        /// <summary>
        /// Maps verbose internal labels to shorter display text for the candidate panel columns.
        /// Internal label strings used by scoring rules are never changed — only the displayed text.
        /// </summary>
        private static string ShortenLabel(string label) => label switch
        {
            "Missing pickup window"      => "No pickup window",
            "Missing delivery window"    => "No delivery window",
            "Missing pickup duration"    => "No pickup duration",
            "Missing delivery duration"  => "No delivery duration",
            "Missing truck availability" => "No free time",
            "Missing truck location"     => "No location",
            "Missing pickup address"     => "No pickup addr",
            "Missing delivery address"   => "No delivery addr",
            "Route estimate missing"     => "No route est.",
            "Not specified"              => "None",
            "Truck type unknown"         => "Type unknown",
            "High deadhead"              => "High deadhead",
            _                            => label
        };

        /// <summary>
        /// Evaluates time feasibility for assigning the selected job to a truck.
        /// Runs through data-availability guards first, then calculates pickup/delivery
        /// slack using EstimateTravelMinutes. Returns a label, display color, and an
        /// optional detail string for the Reason column.
        /// </summary>
        private static (string Label, Color Color, string? Detail) GetTimeFit(Job job, Truck truck)
        {
            var green  = Color.FromRgb( 76, 175,  80);
            var orange = Color.FromRgb(255, 152,   0);
            var red    = Color.FromRgb(255,  92,  53);

            // ── data-availability guards (rules 1-10) ───────────────────────
            if (!truck.IsAvailable)
                return ("Blocked", red, null);
            if (!job.PickupWindowStart.HasValue || !job.PickupWindowEnd.HasValue)
                return ("Missing pickup window", orange, null);
            if (!job.DeliveryWindowStart.HasValue || !job.DeliveryWindowEnd.HasValue)
                return ("Missing delivery window", orange, null);
            if (!job.EstimatedPickupMinutes.HasValue)
                return ("Missing pickup duration", orange, null);
            if (!job.EstimatedDeliveryMinutes.HasValue)
                return ("Missing delivery duration", orange, null);
            if (!truck.AvailableAt.HasValue)
                return ("Missing truck availability", orange, null);
            if (string.IsNullOrWhiteSpace(truck.CurrentLocation))
                return ("Missing truck location", orange, null);
            if (string.IsNullOrWhiteSpace(job.PickupAddress))
                return ("Missing pickup address", orange, null);
            if (string.IsNullOrWhiteSpace(job.DeliveryAddress))
                return ("Missing delivery address", orange, null);

            int? toPickupMin   = EstimateTravelMinutes(truck.CurrentLocation, job.PickupAddress);
            int? toDeliveryMin = EstimateTravelMinutes(job.PickupAddress,     job.DeliveryAddress);

            if (!toPickupMin.HasValue || !toDeliveryMin.HasValue)
                return ("Route estimate missing", orange, null);

            // ── timing calculation ───────────────────────────────────────────
            var pickupArrival   = truck.AvailableAt.Value.AddMinutes(toPickupMin.Value);
            var pickupSlack     = job.PickupWindowEnd!.Value  - pickupArrival;
            var pickupComplete  = pickupArrival.AddMinutes(job.EstimatedPickupMinutes!.Value);
            var deliveryArrival = pickupComplete.AddMinutes(toDeliveryMin.Value);
            var deliverySlack   = job.DeliveryWindowEnd!.Value - deliveryArrival;

            // ── feasibility labels (rules 11-15) ────────────────────────────
            string detail = $"pickup slack {FormatSlack(pickupSlack)}; delivery slack {FormatSlack(deliverySlack)}";

            if (pickupSlack < TimeSpan.Zero)
                return ($"Late pickup by {FormatLate(pickupSlack)}", red, detail);
            if (deliverySlack < TimeSpan.Zero)
                return ($"Late delivery by {FormatLate(deliverySlack)}", red, detail);
            if (pickupSlack <= TimeSpan.FromMinutes(20))
                return ("Tight pickup", orange, detail);
            if (deliverySlack <= TimeSpan.FromMinutes(20))
                return ("Tight delivery", orange, detail);

            return ("Good", green, detail);
        }

        /// <summary>Formats a positive slack TimeSpan as e.g. "45 min" or "1h 20m".</summary>
        private static string FormatSlack(TimeSpan slack)
        {
            var abs = slack.Duration();
            return abs.TotalHours >= 1
                ? $"{(int)abs.TotalHours}h {abs.Minutes}m"
                : $"{(int)abs.TotalMinutes} min";
        }

        /// <summary>Formats the magnitude of a negative slack TimeSpan as e.g. "12 min".</summary>
        private static string FormatLate(TimeSpan negativeSlack) => FormatSlack(negativeSlack);

        /// <summary>
        /// Judges basic route efficiency for a job/truck pair using deadhead vs loaded distance.
        /// DeadheadRatio = toPickup / (toPickup + pickupToDelivery).
        /// Returns a label, display color, and optional detail string.
        /// </summary>
        private static (string Label, Color Color, string? Detail) GetRouteFit(Job job, Truck truck)
        {
            var green    = Color.FromRgb( 76, 175,  80);
            var orange   = Color.FromRgb(255, 152,   0);
            var muted    = Color.FromRgb(140, 140, 140);
            var red      = Color.FromRgb(255,  92,  53);

            // ── data-availability guards ───────────────────────────────────────
            if (!truck.IsAvailable)
                return ("Blocked", red, null);
            if (string.IsNullOrWhiteSpace(truck.CurrentLocation))
                return ("Missing truck location", orange, null);
            if (string.IsNullOrWhiteSpace(job.PickupAddress))
                return ("Missing pickup address", orange, null);
            if (string.IsNullOrWhiteSpace(job.DeliveryAddress))
                return ("Missing delivery address", orange, null);

            int? deadheadMin = EstimateTravelMinutes(truck.CurrentLocation, job.PickupAddress);
            int? loadedMin   = EstimateTravelMinutes(job.PickupAddress,     job.DeliveryAddress);

            if (!deadheadMin.HasValue || !loadedMin.HasValue)
                return ("Route estimate missing", orange, null);

            int total = deadheadMin.Value + loadedMin.Value;
            if (total <= 0)
                return ("Route estimate missing", orange, null);

            // ── efficiency classification ────────────────────────────────────
            double ratio   = (double)deadheadMin.Value / total;
            int    pct     = (int)Math.Round(ratio * 100);
            string detail  = $"deadhead {deadheadMin} min; loaded {loadedMin} min; empty {pct}%";

            if (ratio >= 0.60)
                return ("High deadhead", orange, detail);
            if (ratio >= 0.40)
                return ("Acceptable", muted, detail);

            return ("Efficient", green, detail);
        }

        /// <summary>
        /// Combines Time Fit, Route Fit, and Equipment Fit into a single overall dispatch recommendation.
        /// Rules are evaluated in priority order; the first match wins.
        /// </summary>
        private static (string Label, Color Color) GetOverallFit(
            Truck truck, string timeFitLabel, string routeFitLabel, string equipmentFitLabel)
        {
            var green  = Color.FromRgb( 76, 175,  80);
            var orange = Color.FromRgb(255, 152,   0);
            var muted  = Color.FromRgb(140, 140, 140);
            var red    = Color.FromRgb(255,  92,  53);

            // Rule 0: equipment mismatch makes the job impossible for this truck
            if (equipmentFitLabel == "Mismatch")
                return ("Blocked", red);

            // Rule 1: truck is physically unavailable
            if (!truck.IsAvailable)
                return ("Blocked", red);

            // Rule 2: timing makes the job impossible
            if (timeFitLabel.StartsWith("Late ", StringComparison.Ordinal))
                return ("Blocked", red);

            // Rule 3: required job timing data is absent
            if (timeFitLabel.StartsWith("Missing", StringComparison.Ordinal))
                return ("Unknown", orange);

            // Rule 4: required route data is absent
            if (routeFitLabel.StartsWith("Missing", StringComparison.Ordinal) ||
                routeFitLabel == "Route estimate missing")
                return ("Unknown", orange);

            // Rule 5: timing is feasible but very tight
            if (timeFitLabel is "Tight pickup" or "Tight delivery")
                return ("Risky", orange);

            // Rules 6-8: Good timing — differentiate by route efficiency
            if (timeFitLabel == "Good")
            {
                return routeFitLabel switch
                {
                    "Efficient"    => ("Best",  green),
                    "Acceptable"   => ("Good",  green),
                    "High deadhead"=> ("Poor",  orange),
                    _              => ("Candidate", muted)
                };
            }

            // Rule 9: fallback
            return ("Candidate", muted);
        }

        /// <summary>
        /// Returns a sort rank for an Overall Fit label so candidates are displayed
        /// best-first. Lower rank = displayed higher in the list.
        /// </summary>
        private static int GetOverallFitSortRank(string fitLabel) => fitLabel switch
        {
            "Best"      => 0,
            "Good"      => 1,
            "Risky"     => 2,
            "Candidate" => 3,
            "Poor"      => 4,
            "Unknown"   => 5,
            "Blocked"   => 6,
            _           => 99
        };

        /// <summary>
        /// Compares Job.RequiredEquipment against Truck.VehicleType (case-insensitive).
        /// Returns a label, display color, and an optional compact reason token.
        /// </summary>
        private static (string Label, Color Color, string? Reason) GetEquipmentFit(Job job, Truck truck)
        {
            var green  = Color.FromRgb( 76, 175,  80);
            var orange = Color.FromRgb(255, 152,   0);
            var muted  = Color.FromRgb(140, 140, 140);
            var red    = Color.FromRgb(255,  92,  53);

            if (string.IsNullOrWhiteSpace(job.RequiredEquipment))
                return ("Not specified", muted, null);

            if (string.IsNullOrWhiteSpace(truck.VehicleType))
                return ("Truck type unknown", orange, $"Requires {job.RequiredEquipment}");

            if (truck.VehicleType.Equals(job.RequiredEquipment, StringComparison.OrdinalIgnoreCase))
                return ("Match", green, $"Equipment match: {job.RequiredEquipment}");

            return ("Mismatch", red,
                $"Equipment mismatch: requires {job.RequiredEquipment}, truck is {truck.VehicleType}");
        }

        /// <summary>
        /// Returns a rough travel-time estimate in minutes between two zone strings.
        /// Zones are identified by case-insensitive substring match against known area names.
        /// Returns null when either location cannot be matched to a known zone.
        /// Same-zone trips return 10 minutes. The table is symmetric.
        /// </summary>
        private static int? EstimateTravelMinutes(string? from, string? to)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                return null;

            static string? MatchZone(string text)
            {
                string[] zones = ["Sidney", "Victoria", "Langford", "Nanaimo", "Duncan",
                                  "Saanich", "Colwood", "Oak Bay", "Esquimalt", "Sooke"];
                foreach (var zone in zones)
                    if (text.Contains(zone, StringComparison.OrdinalIgnoreCase))
                        return zone;
                return null;
            }

            var fromZone = MatchZone(from);
            var toZone   = MatchZone(to);

            if (fromZone is null || toZone is null)
                return null;
            if (fromZone == toZone)
                return 10;

            // Canonical key: alphabetical order so the table is symmetric
            string key = string.Compare(fromZone, toZone, StringComparison.Ordinal) < 0
                ? $"{fromZone}|{toZone}"
                : $"{toZone}|{fromZone}";

            return key switch
            {
                "Sidney|Victoria"     => 35,
                "Langford|Sidney"     => 45,
                "Saanich|Sidney"      => 25,
                "Langford|Victoria"   => 25,
                "Saanich|Victoria"    => 15,
                "Esquimalt|Victoria"  => 10,
                "Oak Bay|Victoria"    => 15,
                "Colwood|Langford"    => 10,
                "Langford|Sooke"      => 35,
                "Duncan|Nanaimo"      => 40,
                "Duncan|Victoria"     => 60,
                "Nanaimo|Victoria"    => 95,
                "Nanaimo|Sidney"      => 85,
                "Colwood|Victoria"    => 20,
                "Esquimalt|Langford"  => 20,
                "Duncan|Saanich"      => 55,
                "Duncan|Langford"     => 50,
                "Colwood|Sooke"       => 30,
                "Oak Bay|Saanich"     => 15,
                "Esquimalt|Oak Bay"   => 20,
                _                     => null
            };
        }

        private void StartCurrentTimeMarker()
        {
            if (CurrentTimeMarkerCanvas == null)
                return;

            _currentTimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _currentTimeTimer.Tick += (s, e) => UpdateCurrentTimeMarker();

            // Defer the first update until after the layout pass completes so
            // ScheduleGrid.ActualHeight is non-zero, then start the timer.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateCurrentTimeMarker();
                _currentTimeTimer.Start();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
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
