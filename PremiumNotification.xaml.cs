using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SpeedMeterApp
{
    public partial class PremiumNotification : Window
    {
        private readonly DispatcherTimer _closeTimer;
        private readonly DispatcherTimer _progressTimer;
        private readonly int _duration;
        private double _progressWidth;

        public string NotificationTitle { get; set; } = "Notification";
        public string NotificationMessage { get; set; } = "";
        public string NotificationIcon { get; set; } = "ⓘ";
        public System.Windows.Media.Brush? NotificationBrush { get; set; }
        public System.Windows.Media.Brush? IconBrush { get; set; }

        public PremiumNotification(string title, string message, string icon,
                         System.Windows.Media.Brush brush, System.Windows.Media.Brush iconBrush,
                         int duration = 5000)
        {
            // This must be called first for WPF windows
            InitializeComponent();

            NotificationTitle = title;
            NotificationMessage = message;
            NotificationIcon = icon;
            NotificationBrush = brush;
            IconBrush = iconBrush;
            _duration = duration;

            DataContext = this;

            Debug.WriteLine($"PremiumNotification: {title}");

            // Set up timers
            _closeTimer = new DispatcherTimer();
            _closeTimer.Interval = TimeSpan.FromMilliseconds(_duration);
            _closeTimer.Tick += CloseTimer_Tick;

            _progressTimer = new DispatcherTimer();
            _progressTimer.Interval = TimeSpan.FromMilliseconds(50);
            _progressTimer.Tick += ProgressTimer_Tick;

            Loaded += (s, e) =>
            {
                PositionWindow();
                StartAnimation();

                // Initialize progress bar width - FIXED NULL CHECK
                var progressBorder = (Border?)FindName("ProgressBarFill");
                if (progressBorder != null)
                {
                    _progressWidth = progressBorder.ActualWidth;
                }
                else
                {
                    _progressWidth = 28; // Default width
                }
            };
        }

        private void PositionWindow()
        {
            try
            {
                // Get the primary screen
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                var workingArea = screen.WorkingArea;

                double width = 400;
                double height = 140;

                // Position at top-right corner with some margin
                this.Left = workingArea.Right - width - 20; // 20px from right edge
                this.Top = workingArea.Top + 20; // 20px from top edge
                
                Debug.WriteLine($"Positioning notification at: Left={this.Left}, Top={this.Top}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error positioning window: {ex.Message}");
                // Fallback to system parameters
                this.Left = SystemParameters.WorkArea.Right - 420; // 420 = width + 20
                this.Top = SystemParameters.WorkArea.Top + 20;
            }
        }

        private void StartAnimation()
        {
            // Slide in animation from right
            var slideIn = new ThicknessAnimation
            {
                From = new Thickness(100, 0, -100, 0), // Start from right outside
                To = new Thickness(0),
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.4),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            // Apply animations to the main border
            var mainBorder = (Border?)FindName("MainBorder");
            if (mainBorder != null)
            {
                mainBorder.BeginAnimation(MarginProperty, slideIn);
            }
            
            this.BeginAnimation(OpacityProperty, fadeIn);

            _closeTimer?.Start();
            _progressTimer?.Start();
        }

        private void ProgressTimer_Tick(object? sender, EventArgs e)
        {
            // FIXED: Find the progress bar with null check
            var progressBar = (Border?)FindName("ProgressBarFill");
            if (progressBar != null)
            {
                _progressWidth -= (progressBar.ActualWidth / (_duration / 50));
                progressBar.Width = Math.Max(0, _progressWidth);

                if (_progressWidth <= 0)
                {
                    _progressTimer?.Stop();
                }
            }
        }

        private void CloseTimer_Tick(object? sender, EventArgs e)
        {
            CloseNotification();
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            CloseNotification();
        }

        private void CloseNotification()
        {
            _closeTimer?.Stop();
            _progressTimer?.Stop();

            // Slide out animation to right
            var slideOut = new ThicknessAnimation
            {
                From = new Thickness(0),
                To = new Thickness(100, 0, -100, 0), // Move to right outside
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            slideOut.Completed += (s, e) => Close();
            
            var mainBorder = (Border?)FindName("MainBorder");
            if (mainBorder != null)
            {
                mainBorder.BeginAnimation(MarginProperty, slideOut);
            }
            this.BeginAnimation(OpacityProperty, fadeOut);
        }

        protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            // Pause timers on hover
            _closeTimer?.Stop();
            _progressTimer?.Stop();
        }

        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            // Resume timers when mouse leaves
            _closeTimer?.Start();
            _progressTimer?.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            _closeTimer?.Stop();
            _progressTimer?.Stop();
            base.OnClosed(e);
        }
    }
}