using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DispatchTiger.Models;
using DispatchTiger.Views;
using DispatchTiger.ViewModels;

namespace DispatchTiger
{
    public partial class MainWindow : Window
    {
        private MainViewModel? _viewModel;
        private MapView? _activeMapView;
        private double _currentZoom = 1.0; // Default zoom level: 100%
        private const double MinZoom = 0.60;  // Minimum: 60%
        private const double MaxZoom = 2.0;   // Maximum: 200%
        private const double ZoomStep = 0.10; // Step: 10%
        private ScaleTransform? _currentScaleTransform; // Reference to active view's scale transform

        public MainWindow()
        {
            InitializeComponent();

            // Initialize ViewModel
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // Attach Ctrl + MouseWheel handler
            this.PreviewMouseWheel += MainWindow_PreviewMouseWheel;

            // Show Day View by default
            ShowDayView();
        }

        private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Only handle if Ctrl is pressed
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                // Allow normal scrolling when Ctrl is not pressed
                return;
            }

            // Apply zoom to the current view's scale transform only when Ctrl is pressed
            if (_currentScaleTransform != null)
            {
                // Determine zoom direction
                double zoomDelta = e.Delta > 0 ? ZoomStep : -ZoomStep;
                double newZoom = _currentZoom + zoomDelta;

                // Clamp to min/max range
                if (newZoom < MinZoom)
                    newZoom = MinZoom;
                if (newZoom > MaxZoom)
                    newZoom = MaxZoom;

                // Apply zoom if within valid range
                if (newZoom >= MinZoom && newZoom <= MaxZoom)
                {
                    _currentZoom = newZoom;
                    _currentScaleTransform.ScaleX = _currentZoom;
                    _currentScaleTransform.ScaleY = _currentZoom;

                    // Mark event as handled ONLY after zoom is applied
                    e.Handled = true;
                }
            }
        }

        private void DayViewTab_Click(object sender, RoutedEventArgs e)
        {
            ShowDayView();
        }

        private void MonthViewTab_Click(object sender, RoutedEventArgs e)
        {
            ShowMonthView();
        }

        private void MapViewTab_Click(object sender, RoutedEventArgs e)
        {
            ShowMapView();
        }

        // Resets all tab buttons to the inactive style, then the caller highlights its own.
        private void ResetAllTabStyles()
        {
            var inactive = System.Windows.Media.Brushes.Transparent;
            var inactiveFg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 102, 102));
            DayViewTab.BorderBrush   = inactive;   DayViewTab.Foreground   = inactiveFg;
            MonthViewTab.BorderBrush = inactive;   MonthViewTab.Foreground = inactiveFg;
            MapViewTab.BorderBrush   = inactive;   MapViewTab.Foreground   = inactiveFg;
        }

        private void ShowDayView()
        {
            _activeMapView = null;
            if (_viewModel != null)
                _viewModel.CurrentView = "Day";

            var dayView = new DayView { DataContext = _viewModel };
            ViewContent.Content = dayView;

            // Get the scale transform from the DayView's inner container
            _currentScaleTransform = dayView.FindName("DispatchContentScaleTransform") as ScaleTransform;

            // Reset zoom level when switching views
            _currentZoom = 1.0;
            if (_currentScaleTransform != null)
            {
                _currentScaleTransform.ScaleX = 1.0;
                _currentScaleTransform.ScaleY = 1.0;
            }

            ResetAllTabStyles();
            DayViewTab.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212));
            DayViewTab.Foreground  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212));
        }

        private void ShowMonthView()
        {
            _activeMapView = null;
            if (_viewModel != null)
                _viewModel.CurrentView = "Month";

            var monthView = new MonthView { DataContext = _viewModel };
            ViewContent.Content = monthView;

            // Get the scale transform from the MonthView's inner container
            _currentScaleTransform = monthView.FindName("DispatchContentScaleTransform") as ScaleTransform;

            // Reset zoom level when switching views
            _currentZoom = 1.0;
            if (_currentScaleTransform != null)
            {
                _currentScaleTransform.ScaleX = 1.0;
                _currentScaleTransform.ScaleY = 1.0;
            }

            ResetAllTabStyles();
            MonthViewTab.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212));
            MonthViewTab.Foreground  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212));
        }

        private void ShowMapView()
        {
            if (_viewModel != null)
                _viewModel.CurrentView = "Map";

            // No zoom/scale for Map View
            _currentScaleTransform = null;
            _currentZoom = 1.0;

            var mapView = new MapView { DataContext = _viewModel };
            if (_viewModel != null)
                mapView.SetViewModel(_viewModel);
            _activeMapView = mapView;
            ViewContent.Content = mapView;

            ResetAllTabStyles();
            MapViewTab.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212));
            MapViewTab.Foreground  = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212));
        }

        private void JobBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is Job job && _viewModel != null)
            {
                _viewModel.SelectedJob = job;
                // MapView subscribes to PropertyChanged and self-refreshes;
                // no extra call needed here.
            }
        }
    }
}