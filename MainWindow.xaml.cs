// MainWindow.xaml.cs (FULL merged & cleaned)
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Drawing;                      // for Bitmap, Color (GDI+)
using System.Windows.Threading;            // for DispatcherTimer
using System.Windows.Media.Animation;      // for ColorAnimation
using System.Collections.Generic;
using System.Linq;
using SpeedMeterApp.Models;
using Application = System.Windows.Application;

namespace SpeedMeterApp
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Use fully-qualified names for WinForms types to avoid ambiguous references
        private System.Windows.Forms.NotifyIcon? notifyIcon;
        private System.Threading.Timer? speedTimer;
        private System.Threading.Timer? pingTimer;
        private NetworkInterface[]? networkInterfaces;
        private long previousDownload = 0;
        private long previousUpload = 0;
        private DateTime lastUpdateTime = DateTime.Now;

        // Totals & logging
        private long totalDownloadBytes = 0;
        private long totalUploadBytes = 0;

        private bool isLogging = false;
        private StreamWriter? logWriter;
        private readonly string logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpeedMeterApp");
        private readonly string logFileName;
        private readonly string historyFilePath;

        // Ping
        private string pingHost = "8.8.8.8";
        private int lastPingMs = -1;

        // UI options
        private string displayUnits = "KB"; // "KB" or "MB"
        private int refreshIntervalMs = 1000;

        // Color-sampling fields
        private DispatcherTimer? colorTimer;
        private int colorSampleIntervalMs = 1000; // sample every 1s
        private byte colorBlendAlpha = 0xFF;      // opacity for the border color
        // New fields
        private System.Windows.Media.Color defaultBorderColor = System.Windows.Media.Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A);
        private double sampleBlendFactor = 0.75; // how much sampled color influences final color (0..1)
        private int sampleBoxSize = 3;           // 3x3 sampling box (must be odd)
        // Debug overlay
        private bool debugEnabled = false;   // toggles the debug text visible

        public event PropertyChangedEventHandler? PropertyChanged;

        // Make nullable to avoid CS8618 (set by XAML at runtime typically)
        public object? TxtDebug { get; private set; }
        // Add these lines with your other fields
        private bool _wasConnected = true;
        private System.Windows.Forms.PowerLineStatus _lastPowerStatus = System.Windows.Forms.PowerLineStatus.Offline;
        private int _lastBatteryPercentage = 100;
        private DateTime _lastNetworkCheck = DateTime.MinValue;
        private DateTime _lastBatteryCheck = DateTime.MinValue;
        private System.Windows.Forms.Timer? _monitoringTimer;

        // Add this enum outside the method, inside the class
        public enum NotificationType
        {
            NetworkConnected,
            NetworkDisconnected,
            PowerConnected,
            PowerDisconnected,
            BatteryHigh,
            BatteryMedium,
            BatteryLow,
            BatteryCritical,
            Info,
            Success,
            Warning,
            Error
        }

        // FIXED: Create brushes directly instead of trying to get them from resources
        private (System.Windows.Media.Brush brush, System.Windows.Media.Brush iconBrush, string icon, int duration)
            GetNotificationProperties(NotificationType type)
        {
            return type switch
            {
                NotificationType.NetworkConnected => (
                    CreateGlassGradientBrush("#AA00C853", "#AA00A83E", "#AA008B3A"), // Success gradient
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x69, 0xF0, 0xAE)), // Success icon color
                    "🌐",
                    4000
                ),

                NotificationType.NetworkDisconnected => (
                    CreateGlassGradientBrush("#AAD32F2F", "#AAC62828", "#AAB71C1C"), // Error gradient
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x8A, 0x80)), // Error icon color
                    "📡",
                    5000
                ),

                NotificationType.PowerConnected => (
                    CreateGlassGradientBrush("#AA7C4DFF", "#AA651FFF", "#AA6200EA"), // Power gradient
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB3, 0x88, 0xFF)), // Power icon color
                    "⚡",
                    4000
                ),

                NotificationType.PowerDisconnected => (
                    CreateGlassGradientBrush("#AAFFB300", "#AAFFA000", "#AAFF8F00"), // Warning gradient
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD7, 0x40)), // Warning icon color
                    "🔌",
                    4000
                ),

                NotificationType.BatteryHigh => (
                    CreateGlassGradientBrush("#AA64DD17", "#AA4CAF50", "#AA2E7D32"), // Battery high gradient
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xFF, 0x90)), // Battery high icon color
                    "🔋",
                    4000
                ),

                NotificationType.BatteryMedium => (
                    CreateGlassGradientBrush("#AAFFD600", "#AAFFC107", "#AAFF9800"), // Battery medium gradient
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0x8D)), // Battery medium icon color
                    "🔋",
                    4000
                ),

                NotificationType.BatteryLow => (
                    CreateGlassGradientBrush("#AAFF5722", "#AAD84315", "#AABF360C"), // Battery low gradient
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xAB, 0x91)), // Battery low icon color
                    "🔋",
                    5000
                ),

                NotificationType.BatteryCritical => (
                    CreateGlassGradientBrush("#AADD2C00", "#AAC62828", "#AAB71C1C"), // Battery critical gradient
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x8A, 0x80)), // Battery critical icon color
                    "🔥",
                    7000
                ),

                _ => (
                    CreateGlassGradientBrush("#AA00B8D4", "#AA0097A7", "#AA00838F"), // Default network gradient
                    new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xE5, 0xFF)), // Default icon color
                    "ⓘ",
                    5000
                )
            };
        }

        // Helper method to create glass gradient brushes - UPDATED to use FF (fully opaque)
        private LinearGradientBrush CreateGlassGradientBrush(string color1, string color2, string color3)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1)
            };

            // Changed from AA (semi-transparent) to FF (fully opaque)
            brush.GradientStops.Add(new GradientStop(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color1.Replace("#AA", "#FF")), 0));
            brush.GradientStops.Add(new GradientStop(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color2.Replace("#AA", "#FF")), 0.5));
            brush.GradientStops.Add(new GradientStop(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color3.Replace("#AA", "#FF")), 1));

            return brush;
        }

        // Update the ShowPremiumNotification method
        private void ShowPremiumNotification(string title, string message, NotificationType type)
        {
            try
            {
                Debug.WriteLine($"ShowPremiumNotification: {title} - {type}");

                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var (brush, iconBrush, icon, duration) = GetNotificationProperties(type);

                        var notification = new PremiumNotification(
                            title,
                            message,
                            icon,
                            brush,
                            iconBrush,
                            duration
                        );

                        notification.Show();

                        // Also show system tray notification
                        if (notifyIcon != null)
                        {
                            System.Windows.Forms.ToolTipIcon trayIcon = type switch
                            {
                                NotificationType.NetworkDisconnected or
                                NotificationType.BatteryCritical or
                                NotificationType.Error => System.Windows.Forms.ToolTipIcon.Error,

                                NotificationType.BatteryLow or
                                NotificationType.BatteryMedium or
                                NotificationType.Warning => System.Windows.Forms.ToolTipIcon.Warning,

                                _ => System.Windows.Forms.ToolTipIcon.Info
                            };

                            notifyIcon.ShowBalloonTip(3000, title, message, trayIcon);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error creating notification in UI thread: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Notification error: {ex.Message}");
            }
        }

        // SIMPLIFIED VERSION FOR DEBUGGING
        private void ShowSimpleNotification(string title, string message, NotificationType type)
        {
            try
            {
                Debug.WriteLine($"Simple Notification: {title}");

                Dispatcher.Invoke(() =>
                {
                    // Create a simple notification window without resources
                    var simpleWindow = new Window
                    {
                        Title = title,
                        Width = 400,
                        Height = 120,
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = System.Windows.Media.Brushes.Transparent,
                        Topmost = true,
                        ShowInTaskbar = false,
                        WindowStartupLocation = WindowStartupLocation.Manual
                    };

                    // Position it
                    var screen = System.Windows.Forms.Screen.PrimaryScreen;
                    simpleWindow.Left = screen.WorkingArea.Right - 420;
                    simpleWindow.Top = screen.WorkingArea.Bottom - 140;

                    // Create content
                    var border = new Border
                    {
                        Background = System.Windows.Media.Brushes.DarkBlue,
                        CornerRadius = new CornerRadius(10),
                        Margin = new Thickness(10),
                        Padding = new Thickness(15)
                    };

                    var stack = new StackPanel();
                    stack.Children.Add(new TextBlock
                    {
                        Text = title,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontWeight = FontWeights.Bold,
                        FontSize = 16
                    });

                    stack.Children.Add(new TextBlock
                    {
                        Text = message,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 14,
                        Margin = new Thickness(0, 5, 0, 0)
                    });

                    border.Child = stack;
                    simpleWindow.Content = border;

                    // Show and auto-close
                    simpleWindow.Show();

                    var timer = new DispatcherTimer();
                    timer.Interval = TimeSpan.FromSeconds(3);
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        simpleWindow.Close();
                    };
                    timer.Start();

                    Debug.WriteLine("Simple notification shown!");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Simple notification error: {ex.Message}");
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            // Ensure WPF will not create a taskbar button
            this.ShowInTaskbar = false;

            // Run native style changes after the underlying HWND is created
            this.SourceInitialized += MainWindow_SourceInitialized;

            // Prepare log filename and folder
            Directory.CreateDirectory(logFolder);
            logFileName = Path.Combine(logFolder, $"speedlog_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            historyFilePath = Path.Combine(logFolder, "history.csv");
            // load existing history if any
            LoadHistoryFromCsv();

            InitializeNetworkMonitoring();
            SetupTrayIcon();
            StartHistoryFlushTimer();
            PositionWindowTopRight();

            // Hook drag event if the border exists in XAML
            try
            {
                if (MainBorder != null)
                    MainBorder.MouseLeftButtonDown += MainBorder_MouseLeftButtonDown;
            }
            catch
            {
                // ignore
            }

            // Initialize monitoring timers with a delay to ensure window is loaded
            this.Loaded += (s, e) =>
            {
                // Start with a small delay to ensure everything is initialized
                DispatcherTimer initTimer = new DispatcherTimer();
                initTimer.Interval = TimeSpan.FromSeconds(1);
                initTimer.Tick += (timerSender, timerArgs) =>
                {
                    initTimer.Stop();
                    StartMonitoringTimers();

                    // Test that notifications work
                    Debug.WriteLine("Initializing - testing notification system...");
                    ShowSimpleNotification("SpeedMeter", "Application started successfully", NotificationType.Info);
                };
                initTimer.Start();
            };
        }

        // ----- History storage (24-hour rolling) -----
        private struct SampleEntry
        {
            public DateTime Timestamp;         // UTC
            public long TotalDownloadBytes;    // cumulative
            public long TotalUploadBytes;      // cumulative
            public double DownloadKBps;        // instant KB/s
            public double UploadKBps;          // instant KB/s
        }

        private readonly List<SampleEntry> history = new List<SampleEntry>();
        private readonly object historyLock = new object();
        private DispatcherTimer? historyFlushTimer;
        private int historyFlushIntervalSeconds = 60; // flush every minute
        private int historyRetentionHours = 24; // keep 24 hours

        /// <summary>
        /// Load history CSV (if exists). CSV format: ISO timestamp (UTC),TotalDownloadBytes,TotalUploadBytes,DownloadKBps,UploadKBps
        /// </summary>
        private void LoadHistoryFromCsv()
        {
            try
            {
                if (string.IsNullOrEmpty(historyFilePath) || !File.Exists(historyFilePath)) return;

                var lines = File.ReadAllLines(historyFilePath);
                var list = new List<SampleEntry>(lines.Length);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(',');
                    if (parts.Length < 5) continue;

                    if (!DateTime.TryParse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime ts)) continue;
                    if (!long.TryParse(parts[1], out long td)) continue;
                    if (!long.TryParse(parts[2], out long tu)) continue;
                    if (!double.TryParse(parts[3], out double dk)) continue;
                    if (!double.TryParse(parts[4], out double uk)) continue;

                    list.Add(new SampleEntry
                    {
                        Timestamp = ts.ToUniversalTime(),
                        TotalDownloadBytes = td,
                        TotalUploadBytes = tu,
                        DownloadKBps = dk,
                        UploadKBps = uk
                    });
                }

                lock (historyLock)
                {
                    history.Clear();
                    history.AddRange(list.OrderBy(s => s.Timestamp));
                    TrimHistoryLocked();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadHistoryFromCsv error: {ex.Message}");
            }
        }

        /// <summary>
        /// Append a sample (called from UpdateNetworkSpeeds). Keeps only last 24 hours of data.
        /// </summary>
        private void AppendHistorySample(long totalDownloadBytesCumulative, long totalUploadBytesCumulative, double downloadKBps, double uploadKBps)
        {
            try
            {
                var s = new SampleEntry
                {
                    Timestamp = DateTime.UtcNow,
                    TotalDownloadBytes = totalDownloadBytesCumulative,
                    TotalUploadBytes = totalUploadBytesCumulative,
                    DownloadKBps = downloadKBps,
                    UploadKBps = uploadKBps
                };

                lock (historyLock)
                {
                    // avoid duplicates: if last timestamp within same second, replace it
                    if (history.Count > 0)
                    {
                        var last = history[history.Count - 1];
                        if ((s.Timestamp - last.Timestamp).TotalSeconds < 1.0)
                        {
                            history[history.Count - 1] = s;
                        }
                        else
                        {
                            history.Add(s);
                        }
                    }
                    else
                    {
                        history.Add(s);
                    }

                    TrimHistoryLocked();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppendHistorySample error: {ex.Message}");
            }
        }

        /// <summary>
        /// Trim history to retention window (24 hours) - must be called under historyLock
        /// </summary>
        private void TrimHistoryLocked()
        {
            DateTime cutoff = DateTime.UtcNow.AddHours(-historyRetentionHours);
            while (history.Count > 0 && history[0].Timestamp < cutoff)
                history.RemoveAt(0);
        }

        /// <summary>
        /// Save history to CSV (overwrites). Called periodically by a timer and on exit.
        /// </summary>
        private void SaveHistoryToCsv()
        {
            try
            {
                List<SampleEntry> snapshot;
                lock (historyLock)
                {
                    snapshot = history.ToList();
                }

                Directory.CreateDirectory(Path.GetDirectoryName(historyFilePath) ?? logFolder);
                using (var sw = new StreamWriter(historyFilePath, false))
                {
                    foreach (var s in snapshot)
                    {
                        // ISO 8601 (round-trip) format for timestamp
                        sw.WriteLine($"{s.Timestamp:o},{s.TotalDownloadBytes},{s.TotalUploadBytes},{s.DownloadKBps:F3},{s.UploadKBps:F3}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveHistoryToCsv error: {ex.Message}");
            }
        }

        /// <summary>
        /// Start the periodic flush timer (called from ctor)
        /// </summary>
        private void StartHistoryFlushTimer()
        {
            try
            {
                if (historyFlushTimer != null)
                {
                    historyFlushTimer.Stop();
                    historyFlushTimer = null;
                }

                historyFlushTimer = new DispatcherTimer();
                historyFlushTimer.Interval = TimeSpan.FromSeconds(historyFlushIntervalSeconds);
                historyFlushTimer.Tick += (s, e) => SaveHistoryToCsv();
                historyFlushTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartHistoryFlushTimer error: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop flush timer and force-save once
        /// </summary>
        private void StopHistoryFlushTimer()
        {
            try
            {
                if (historyFlushTimer != null)
                {
                    historyFlushTimer.Stop();
                    historyFlushTimer = null;
                }
                SaveHistoryToCsv();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StopHistoryFlushTimer error: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns total bytes transferred in last 24 hours (difference between first and last samples inside window).
        /// </summary>
        private (long download24hBytes, long upload24hBytes) Get24HourTotal()
        {
            DateTime cutoff = DateTime.UtcNow.AddHours(-historyRetentionHours);
            lock (historyLock)
            {
                var window = history.Where(h => h.Timestamp >= cutoff).ToList();
                if (window.Count >= 2)
                {
                    var first = window.First();
                    var last = window.Last();
                    return (Math.Max(0, last.TotalDownloadBytes - first.TotalDownloadBytes),
                            Math.Max(0, last.TotalUploadBytes - first.TotalUploadBytes));
                }
                // not enough samples in the window -> fallback to entire history difference
                if (history.Count >= 2)
                {
                    var first = history.First();
                    var last = history.Last();
                    return (Math.Max(0, last.TotalDownloadBytes - first.TotalDownloadBytes),
                            Math.Max(0, last.TotalUploadBytes - first.TotalUploadBytes));
                }
                return (0L, 0L);
            }
        }

        /// <summary>
        /// Returns 24 hourly buckets covering last 24 hours. Each tuple: (bucketStartUtc, downloadBytesInBucket, uploadBytesInBucket)
        /// </summary>
        private (DateTime bucketStartUtc, long downloadBytes, long uploadBytes)[] GetHourlyBuckets24h()
        {
            var buckets = new (DateTime, long, long)[24];
            DateTime now = DateTime.UtcNow;
            DateTime oldestBucketStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(-23);

            lock (historyLock)
            {
                for (int i = 0; i < 24; i++)
                {
                    DateTime start = oldestBucketStart.AddHours(i);
                    DateTime end = start.AddHours(1);

                    // find last sample <= start and last sample <= end
                    var beforeOrAtStart = history.LastOrDefault(h => h.Timestamp <= start);
                    var beforeOrAtEnd = history.LastOrDefault(h => h.Timestamp <= end);

                    if (beforeOrAtEnd.Timestamp == default && history.Count > 0)
                        beforeOrAtEnd = history.First();

                    if (beforeOrAtStart.Timestamp == default && history.Count > 0)
                        beforeOrAtStart = history.First();

                    long d = 0, u = 0;
                    if (beforeOrAtEnd.Timestamp != default && beforeOrAtStart.Timestamp != default)
                    {
                        d = Math.Max(0, beforeOrAtEnd.TotalDownloadBytes - beforeOrAtStart.TotalDownloadBytes);
                        u = Math.Max(0, beforeOrAtEnd.TotalUploadBytes - beforeOrAtStart.TotalUploadBytes);
                    }

                    buckets[i] = (start, d, u);
                }
            }

            return buckets;
        }

        private void ExportHistoryCsv(string targetPath)
        {
            try
            {
                List<SampleEntry> snapshot;
                lock (historyLock)
                {
                    snapshot = history.ToList();
                }

                using (var sw = new StreamWriter(targetPath, false))
                {
                    sw.WriteLine("TimestampUtc,TotalDownloadBytes,TotalUploadBytes,DownloadKBps,UploadKBps");
                    foreach (var s in snapshot)
                    {
                        sw.WriteLine($"{s.Timestamp:o},{s.TotalDownloadBytes},{s.TotalUploadBytes},{s.DownloadKBps:F3},{s.UploadKBps:F3}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ExportHistoryCsv error: {ex.Message}");
            }
        }

        // ---------------- Dashboard UI ----------------
        private void ShowHistoryWindow()
        {
            try
            {
                // snapshot
                List<SampleEntry> snapshot;
                lock (historyLock)
                {
                    snapshot = history.ToList();
                }

                var win = new Window
                {
                    Title = "SpeedMeterApp — History (last 24h)",
                    Width = 800,
                    Height = 520,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(260) }); // chart
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // grid + controls

                // Top: simple chart area (draw two polylines for download/upload)
                var chartCanvas = new Canvas { Background = System.Windows.Media.Brushes.Transparent, Margin = new Thickness(10) };
                Grid.SetRow(chartCanvas, 0);

                // Build points from hourly buckets
                var buckets = GetHourlyBuckets24h();
                long maxBytes = Math.Max(1, buckets.Max(b => Math.Max(b.downloadBytes, b.uploadBytes)));

                // Draw axes labels
                for (int i = 0; i < buckets.Length; i++)
                {
                    double x = 10 + (i * ((win.Width - 60) / 23.0));
                    var tb = new TextBlock { Text = buckets[i].bucketStartUtc.ToLocalTime().ToString("HH:mm"), FontSize = 10, Foreground = System.Windows.Media.Brushes.Gray };
                    Canvas.SetLeft(tb, x - 10);
                    Canvas.SetTop(tb, 230);
                    chartCanvas.Children.Add(tb);
                }

                // helper to create polyline points
                PointCollection downloadPoints = new PointCollection();
                PointCollection uploadPoints = new PointCollection();

                for (int i = 0; i < buckets.Length; i++)
                {
                    double x = 10 + (i * ((win.Width - 60) / 23.0));
                    double dh = 200.0 * (double)buckets[i].downloadBytes / (double)maxBytes;
                    double uh = 200.0 * (double)buckets[i].uploadBytes / (double)maxBytes;
                    downloadPoints.Add(new System.Windows.Point(x, 220 - dh));
                    uploadPoints.Add(new System.Windows.Point(x, 220 - uh));
                }

                var downloadLine = new System.Windows.Shapes.Polyline { Points = downloadPoints, StrokeThickness = 2, Stroke = System.Windows.Media.Brushes.CornflowerBlue };
                var uploadLine = new System.Windows.Shapes.Polyline { Points = uploadPoints, StrokeThickness = 2, Stroke = System.Windows.Media.Brushes.LightGreen };

                chartCanvas.Children.Add(downloadLine);
                chartCanvas.Children.Add(uploadLine);

                // Legend
                var leg1 = new TextBlock { Text = "Download", Foreground = System.Windows.Media.Brushes.CornflowerBlue, Margin = new Thickness(10, 4, 0, 0) };
                Canvas.SetLeft(leg1, 10); Canvas.SetTop(leg1, 6);
                var leg2 = new TextBlock { Text = "Upload", Foreground = System.Windows.Media.Brushes.LightGreen, Margin = new Thickness(80, 4, 0, 0) };
                Canvas.SetLeft(leg2, 90); Canvas.SetTop(leg2, 6);
                chartCanvas.Children.Add(leg1);
                chartCanvas.Children.Add(leg2);

                grid.Children.Add(chartCanvas);

                // bottom: DataGrid + controls
                var bottomGrid = new Grid { Margin = new Thickness(10) };
                bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

                var dg = new DataGrid
                {
                    IsReadOnly = true,
                    AutoGenerateColumns = false,
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    ItemsSource = snapshot.OrderByDescending(s => s.Timestamp).ToList()
                };

                dg.Columns.Add(new DataGridTextColumn { Header = "Timestamp (Local)", Binding = new System.Windows.Data.Binding("Timestamp") { StringFormat = "g" } });
                dg.Columns.Add(new DataGridTextColumn { Header = "Download KB/s", Binding = new System.Windows.Data.Binding("DownloadKBps") { StringFormat = "F2" } });
                dg.Columns.Add(new DataGridTextColumn { Header = "Upload KB/s", Binding = new System.Windows.Data.Binding("UploadKBps") { StringFormat = "F2" } });
                dg.Columns.Add(new DataGridTextColumn { Header = "Total Download", Binding = new System.Windows.Data.Binding("TotalDownloadBytes") });
                dg.Columns.Add(new DataGridTextColumn { Header = "Total Upload", Binding = new System.Windows.Data.Binding("TotalUploadBytes") });

                Grid.SetColumn(dg, 0);

                var rightPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical, Margin = new Thickness(8) };
                Grid.SetColumn(rightPanel, 1);

                var btnExport = new System.Windows.Controls.Button { Content = "Export CSV", Margin = new Thickness(0, 0, 0, 6) };
                btnExport.Click += (s, e) =>
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "speed_history.csv", Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
                    if (dlg.ShowDialog() == true)
                    {
                        ExportHistoryCsv(dlg.FileName);
                        System.Windows.MessageBox.Show($"History exported to {dlg.FileName}");
                    }
                };

                var (d24, u24) = Get24HourTotal();
                var txtSummary = new TextBlock { Text = $"Last 24h\nDown: {FormatTotal(d24)}\nUp: {FormatTotal(u24)}", Margin = new Thickness(0, 12, 0, 12) };

                rightPanel.Children.Add(btnExport);
                rightPanel.Children.Add(txtSummary);

                bottomGrid.Children.Add(dg);
                bottomGrid.Children.Add(rightPanel);

                Grid.SetRow(bottomGrid, 1);
                grid.Children.Add(bottomGrid);

                win.Content = grid;
                win.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowHistoryWindow error: {ex.Message}");
                System.Windows.MessageBox.Show($"Could not open history: {ex.Message}");
            }
        }

        // Ensure this is placed at class scope (not inside another method)
        private void PositionWindowTopRight()
        {
            var workingArea = SystemParameters.WorkArea;
            // Protect against uninitialized Width/Height by using ActualWidth/ActualHeight if needed
            double w = (this.Width > 0) ? this.Width : this.ActualWidth;
            double h = (this.Height > 0) ? this.Height : this.ActualHeight;

            // Fallback to defaults if still zero
            if (w <= 0) w = 110;
            if (h <= 0) h = 65;

            this.Left = workingArea.Right - w - 8;
            this.Top = workingArea.Top + 8;
            this.WindowStartupLocation = WindowStartupLocation.Manual;
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Double-safety: ensure ShowInTaskbar is false once window is initialized
            this.ShowInTaskbar = false;

            // Apply the native style changes
            HideFromAltTab();

            // start color sampling
            StartColorSampling();
        }

        // Constants for GetWindowLong/SetWindowLong indices
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        private void HideFromAltTab()
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hWnd = helper.Handle;
            if (hWnd == IntPtr.Zero) return;

            // Get current extended style
            IntPtr exStylePtr = GetWindowLongPtrCompat(hWnd, GWL_EXSTYLE);
            long exStyle = exStylePtr.ToInt64();

            // Remove WS_EX_APPWINDOW and add WS_EX_TOOLWINDOW
            exStyle &= ~((long)WS_EX_APPWINDOW);
            exStyle |= ((long)WS_EX_TOOLWINDOW);

            // Write it back
            SetWindowLongPtrCompat(hWnd, GWL_EXSTYLE, new IntPtr(exStyle));
        }

        // 64/32-bit safe wrappers
        private static IntPtr GetWindowLongPtrCompat(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
            {
                return GetWindowLongPtr64(hWnd, nIndex);
            }
            else
            {
                return new IntPtr(GetWindowLong32(hWnd, nIndex));
            }
        }

        private static IntPtr SetWindowLongPtrCompat(IntPtr hWnd, int nIndex, IntPtr newValue)
        {
            if (IntPtr.Size == 8)
            {
                return SetWindowLongPtr64(hWnd, nIndex, newValue);
            }
            else
            {
                return new IntPtr(SetWindowLong32(hWnd, nIndex, newValue.ToInt32()));
            }
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        // Start/Stop sampling helpers — place these at class scope (e.g. after PositionWindowTopRight)
        private void StartColorSampling()
        {
            try
            {
                // capture default color from MainBorder if present
                try
                {
                    if (MainBorder?.Background is SolidColorBrush scb)
                    {
                        defaultBorderColor = scb.Color;
                    }
                }
                catch { /* ignore */ }

                // stop any previous timer safely
                if (colorTimer != null)
                {
                    colorTimer.Stop();
                    colorTimer.Tick -= ColorTimer_Tick;
                    colorTimer = null;
                }

                colorTimer = new DispatcherTimer();
                colorTimer.Interval = TimeSpan.FromMilliseconds(colorSampleIntervalMs);
                colorTimer.Tick += ColorTimer_Tick;
                colorTimer.Start();

                // immediate sample if visible
                if (this.IsVisible)
                    SampleAndApplyColor();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartColorSampling error: {ex.Message}");
            }
        }

        private void StopColorSampling()
        {
            try
            {
                if (colorTimer != null)
                {
                    colorTimer.Stop();
                    colorTimer.Tick -= ColorTimer_Tick;
                    colorTimer = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StopColorSampling error: {ex.Message}");
            }
        }

        // single tick handler separated so we can add/remove the event easily
        private void ColorTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (!this.IsVisible) return;
                SampleAndApplyColor();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ColorTimer_Tick error: {ex.Message}");
            }
        }

        // ------------------- Dynamic border color (best-practice version) -------------------

        // Start sampling (call after window initialized)
        private void SampleAndApplyColor()
        {
            string decision = "n/a";
            System.Windows.Media.Color sampledColor = defaultBorderColor;
            double hue = 0, sat = 0, val = 0;

            try
            {
                // get DPI scale
                var dpi = VisualTreeHelper.GetDpi(this);
                double scaleX = dpi.DpiScaleX;
                double scaleY = dpi.DpiScaleY;

                // use actual window center in screen coords
                double centerX = this.Left + (this.Width / 2.0);
                double centerY = this.Top + (this.Height / 2.0);

                // convert to device pixels
                int screenCenterX = Math.Max(0, (int)Math.Round(centerX * scaleX));
                int screenCenterY = Math.Max(0, (int)Math.Round(centerY * scaleY));

                int half = Math.Max(0, (sampleBoxSize - 1) / 2);
                long totalR = 0, totalG = 0, totalB = 0;
                int samples = 0;

                using (var bmp = new Bitmap(sampleBoxSize, sampleBoxSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        int srcX = screenCenterX - half;
                        int srcY = screenCenterY - half;
                        srcX = Math.Max(0, srcX);
                        srcY = Math.Max(0, srcY);

                        // Copy that small box from screen
                        g.CopyFromScreen(srcX, srcY, 0, 0, new System.Drawing.Size(sampleBoxSize, sampleBoxSize));
                    }

                    for (int y = 0; y < sampleBoxSize; y++)
                    {
                        for (int x = 0; x < sampleBoxSize; x++)
                        {
                            var c = bmp.GetPixel(x, y);
                            totalR += c.R;
                            totalG += c.G;
                            totalB += c.B;
                            samples++;
                        }
                    }
                }

                if (samples == 0) return;

                byte avgR = (byte)(totalR / samples);
                byte avgG = (byte)(totalG / samples);
                byte avgB = (byte)(totalB / samples);

                sampledColor = System.Windows.Media.Color.FromArgb(0xFF, avgR, avgG, avgB);

                // ---------- Heuristics ----------

                // 1) Low contrast / nearly gray -> default
                int maxRGB = Math.Max(avgR, Math.Max(avgG, avgB));
                int minRGB = Math.Min(avgR, Math.Min(avgG, avgB));
                int diff = maxRGB - minRGB;
                if (diff < 30) // low colorfulness
                {
                    decision = "low colorfulness -> default";
                    ApplyBorderColorAnimated(defaultBorderColor);
                    UpdateDebugOverlay(sampledColor, hue, sat, val, decision);
                    return;
                }

                // 2) Too dark or too bright -> default
                int brightness = (avgR + avgG + avgB) / 3;
                if (brightness < 35 || brightness > 240)
                {
                    decision = $"brightness {brightness} -> default";
                    ApplyBorderColorAnimated(defaultBorderColor);
                    UpdateDebugOverlay(sampledColor, hue, sat, val, decision);
                    return;
                }

                // Balanced: main thresholds, but allow very strong red/green/blue dominance
                if (sat < 0.30 || val < 0.18)
                {
                    // Check if one channel is strongly dominant (e.g., pure-ish red / green / blue)
                    int maxChannel = Math.Max(avgR, Math.Max(avgG, avgB));
                    int minChannel = Math.Min(avgR, Math.Min(avgG, avgB));
                    int dominance = maxChannel - minChannel; // how dominant the top channel is

                    // Allow through if dominance is very high (i.e., almost single-channel)
                    if (dominance < 60) // if not very dominant => default
                    {
                        decision = $"sat {sat:F2} val {val:F2} too low and dominance {dominance} < 60 -> default (balanced)";
                        ApplyBorderColorAnimated(defaultBorderColor);
                        UpdateDebugOverlay(sampledColor, hue, sat, val, decision);
                        return;
                    }
                    // otherwise fall through and accept (dominant primary)
                }

                // Allowed color: blend and apply
                var blended = BlendColors(defaultBorderColor, sampledColor, sampleBlendFactor);
                blended.A = colorBlendAlpha;
                ApplyBorderColorAnimated(blended);
                decision = $"accepted (blend)";
                UpdateDebugOverlay(sampledColor, hue, sat, val, decision);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SampleAndApplyColor error: {ex.Message}");
                UpdateDebugOverlay(sampledColor, hue, sat, val, $"error: {ex.Message}");
            }
        }

        private void UpdateDebugOverlay(System.Windows.Media.Color sampled, double hue, double sat, double val, string decision)
        {
            if (!debugEnabled || TxtDebugOverlay == null) return;

            string hex = $"#{sampled.R:X2}{sampled.G:X2}{sampled.B:X2}";
            string lines =
                $"Sampled: {hex} (R{sampled.R} G{sampled.G} B{sampled.B})\n" +
                $"HSV: H={hue:F0}°, S={sat:F2}, V={val:F2}\n" +
                $"Decision: {decision}\n" +
                $"Blend: {sampleBlendFactor:F2}  Box: {sampleBoxSize}x{sampleBoxSize}";

            Dispatcher.Invoke(() =>
            {
                TxtDebugOverlay.Text = lines;
            });
        }

        // Smooth animation helper (keeps or creates a mutable SolidColorBrush)
        private void ApplyBorderColorAnimated(System.Windows.Media.Color targetColor, int durationMs = 300)
        {
            try
            {
                if (MainBorder == null) return;

                var currentBrush = MainBorder.Background as SolidColorBrush;
                if (currentBrush == null)
                {
                    currentBrush = new SolidColorBrush(defaultBorderColor);
                    MainBorder.Background = currentBrush;
                }

                if (currentBrush.IsFrozen)
                {
                    currentBrush = currentBrush.Clone();
                    MainBorder.Background = currentBrush;
                }

                var colorAnim = new ColorAnimation
                {
                    To = targetColor,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                currentBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyBorderColorAnimated error: {ex.Message}");
            }
        }

        // Small helpers
        private static double ColorDistance(System.Windows.Media.Color a, System.Windows.Media.Color b)
        {
            double dr = a.R - b.R;
            double dg = a.G - b.G;
            double db = a.B - b.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static System.Windows.Media.Color BlendColors(System.Windows.Media.Color baseColor, System.Windows.Media.Color sampledColor, double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            byte a = (byte)(baseColor.A + (sampledColor.A - baseColor.A) * t);
            byte r = (byte)(baseColor.R + (sampledColor.R - baseColor.R) * t);
            byte g = (byte)(baseColor.G + (sampledColor.G - baseColor.G) * t);
            byte b = (byte)(baseColor.B + (sampledColor.B - baseColor.B) * t);
            return System.Windows.Media.Color.FromArgb(a, r, g, b);
        }

        private void InitializeNetworkMonitoring()
        {
            try
            {
                networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                speedTimer = new System.Threading.Timer(UpdateNetworkSpeeds!, null, 0, refreshIntervalMs);
                pingTimer = new System.Threading.Timer(UpdatePing!, null, 0, 5000);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error initializing network monitoring: {ex.Message}");
            }
        }

        private async void UpdatePing(object? state)
        {
            try
            {
                using (var p = new Ping())
                {
                    var reply = await p.SendPingAsync(pingHost, 2000).ConfigureAwait(false);
                    lastPingMs = reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
                }

                // update tray tooltip to include ping
                if (notifyIcon != null)
                {
                    notifyIcon.Text = lastPingMs >= 0 ? $"Ping: {lastPingMs} ms" : "Ping: N/A";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ping error: {ex.Message}");
            }
        }

        private void UpdateNetworkSpeeds(object state)
        {
            try
            {
                if (networkInterfaces == null) return;

                long totalDownload = 0;
                long totalUpload = 0;

                foreach (var ni in networkInterfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var stats = ni.GetIPv4Statistics();
                        totalDownload += stats.BytesReceived;
                        totalUpload += stats.BytesSent;
                    }
                }

                DateTime currentTime = DateTime.Now;
                double timeDifference = (currentTime - lastUpdateTime).TotalSeconds;
                lastUpdateTime = currentTime;
                if (timeDifference <= 0.1) timeDifference = 1.0;

                long downloadDifference = totalDownload - previousDownload;
                long uploadDifference = totalUpload - previousUpload;
                previousDownload = totalDownload;
                previousUpload = totalUpload;

                if (downloadDifference > 0) totalDownloadBytes += downloadDifference;
                if (uploadDifference > 0) totalUploadBytes += uploadDifference;

                double downloadSpeedKbps = downloadDifference / 1024.0 / timeDifference;
                double uploadSpeedKbps = uploadDifference / 1024.0 / timeDifference;

                // Logging
                if (isLogging)
                {
                    try
                    {
                        if (logWriter == null)
                        {
                            logWriter = new StreamWriter(logFileName, true);
                            logWriter.WriteLine("Timestamp,Download_KBps,Upload_KBps,Ping_ms,TotalDownloadBytes,TotalUploadBytes");
                            logWriter.Flush();
                        }

                        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{downloadSpeedKbps:F2},{uploadSpeedKbps:F2},{lastPingMs},{totalDownloadBytes},{totalUploadBytes}";
                        logWriter.WriteLine(line);
                        logWriter.Flush();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Logging error: {ex.Message}");
                    }
                }

                // Update UI
                Dispatcher.Invoke(() =>
                {
                    if (TxtDownload != null) TxtDownload.Text = $"D: {FormatSpeed(downloadSpeedKbps)}";
                    if (TxtUpload != null) TxtUpload.Text = $"U: {FormatSpeed(uploadSpeedKbps)}";
                    if (TxtTime != null) TxtTime.Text = currentTime.ToString("hh:mm:ss tt");

                    if (notifyIcon != null)
                    {
                        string totals = $"T: {FormatTotal(totalDownloadBytes)}/{FormatTotal(totalUploadBytes)}";
                        notifyIcon.Text = lastPingMs >= 0 ? $"Ping: {lastPingMs} ms • {totals}" : totals;
                    }
                });

                // append a history sample (thread-safe)
                try
                {
                    AppendHistorySample(totalDownloadBytes, totalUploadBytes, downloadSpeedKbps, uploadSpeedKbps);
                }
                catch { /* ignore history errors */ }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating speeds: {ex.Message}");
            }
        }

        private string FormatSpeed(double kbps)
        {
            if (displayUnits == "MB")
            {
                double mbps = kbps / 1024.0;
                if (mbps >= 1024) return $"{mbps / 1024:F2} GB/s";
                return $"{mbps:F2} MB/s";
            }
            else
            {
                if (kbps > 1024 * 1024) return $"{kbps / 1024 / 1024:F2} GB/s";
                if (kbps > 1024) return $"{kbps / 1024:F2} MB/s";
                return $"{kbps:F2} KB/s";
            }
        }

        private string FormatTotal(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            if (bytes >= 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F2} MB";
            if (bytes >= 1024L) return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }

        private void SetupTrayIcon()
        {
            try
            {
                notifyIcon = new System.Windows.Forms.NotifyIcon();
                notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                notifyIcon.Text = "Network Speed Meter";
                notifyIcon.Visible = true;

                var contextMenu = new System.Windows.Forms.ContextMenuStrip();

                // Create menu items
                var hideMenuItem = new System.Windows.Forms.ToolStripMenuItem("Hide", null, (s, e) => HideWindow());
                var showMenuItem = new System.Windows.Forms.ToolStripMenuItem("Show", null, (s, e) => ShowWindow());

                // "Always on Top" with checkmark
                var alwaysOnTopMenuItem = new System.Windows.Forms.ToolStripMenuItem("Always on Top", null, ToggleAlwaysOnTop!);
                alwaysOnTopMenuItem.Checked = this.Topmost; // Set initial check state

                // "Start with Windows" with checkmark
                var startWithWindowsMenuItem = new System.Windows.Forms.ToolStripMenuItem("Start with Windows", null, ToggleStartup!);
                startWithWindowsMenuItem.Checked = IsStartupEnabled(); // Check if already in startup

                // Add items to menu
                contextMenu.Items.Add(hideMenuItem);
                contextMenu.Items.Add(showMenuItem);
                contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                contextMenu.Items.Add(alwaysOnTopMenuItem);
                contextMenu.Items.Add(startWithWindowsMenuItem);
                contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

                // Rest of menu items (keep your existing ones)
                contextMenu.Items.Add("Toggle Logging", null, (s, e) => ToggleLogging());
                contextMenu.Items.Add("Ping Now", null, (s, e) => TriggerPingNow());
                contextMenu.Items.Add("Show Totals", null, (s, e) => ShowTotals());
                contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                contextMenu.Items.Add("Change Background", null, ChangeBackgroundColor!);
                contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                contextMenu.Items.Add("Connection Info", null, ShowConnectionInfo!);
                contextMenu.Items.Add("Refresh Network", null, RefreshNetwork!);
                contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                contextMenu.Items.Add("📊 Premium Dashboard", null, (s, e) => ShowPremiumDashboard());
                contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                contextMenu.Items.Add("Exit", null, ExitApplication!);

                notifyIcon.ContextMenuStrip = contextMenu;

                notifyIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == System.Windows.Forms.MouseButtons.Right)
                    {
                        // Update checkmarks before showing menu
                        UpdateMenuCheckmarks(contextMenu);
                        contextMenu.Show(System.Windows.Forms.Cursor.Position);
                    }
                    else if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    {
                        ToggleWindowVisibility();
                    }
                };

                notifyIcon.DoubleClick += (s, e) => ToggleWindowVisibility();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error setting up tray icon: {ex.Message}");
            }
        }

        private void UpdateMenuCheckmarks(System.Windows.Forms.ContextMenuStrip menu)
        {
            if (menu == null) return;

            // Find and update "Always on Top" checkmark
            foreach (System.Windows.Forms.ToolStripItem item in menu.Items)
            {
                if (item is System.Windows.Forms.ToolStripMenuItem menuItem)
                {
                    if (menuItem.Text == "Always on Top")
                    {
                        menuItem.Checked = this.Topmost;
                    }
                    else if (menuItem.Text == "Start with Windows")
                    {
                        menuItem.Checked = IsStartupEnabled();
                    }
                    else if (menuItem.Text == "Hide" || menuItem.Text == "Show")
                    {
                        // Update Hide/Show based on window visibility
                        if (this.IsVisible)
                        {
                            if (menuItem.Text == "Hide") menuItem.Enabled = true;
                            if (menuItem.Text == "Show") menuItem.Enabled = false;
                        }
                        else
                        {
                            if (menuItem.Text == "Hide") menuItem.Enabled = false;
                            if (menuItem.Text == "Show") menuItem.Enabled = true;
                        }
                    }
                }
            }
        }

        private bool IsStartupEnabled()
        {
            try
            {
                string appName = "SpeedMeterApp";
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        return key.GetValue(appName) != null;
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
            return false;
        }

        // In MainWindow.xaml.cs, update the method that creates DashboardWindow:
        private void ShowPremiumDashboard()
        {
            try
            {
                // Convert from local SampleEntry to Models.SampleEntry
                var snapshot = new List<SpeedMeterApp.Models.SampleEntry>();
                lock (historyLock)
                {
                    snapshot = history.Select(s => new SpeedMeterApp.Models.SampleEntry
                    {
                        Timestamp = s.Timestamp,
                        TotalDownloadBytes = s.TotalDownloadBytes,
                        TotalUploadBytes = s.TotalUploadBytes,
                        DownloadKBps = s.DownloadKBps,
                        UploadKBps = s.UploadKBps
                    }).ToList();
                }

                var dashboard = new DashboardWindow(snapshot, this);
                dashboard.Show();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not open dashboard: {ex.Message}");
            }
        }

        private void ShowWindow()
        {
            PositionWindowTopRight();
            this.Show();
            this.WindowState = WindowState.Normal;
            // restart sampling (if it was stopped)
            StartColorSampling();

            // Make sure it's on top
            this.Topmost = true;

            // Update menu - ADD THIS LINE
            UpdateHideShowMenu();
        }

        private void HideWindow()
        {
            this.Hide();
            StopColorSampling();

            // Update menu - ADD THIS LINE
            UpdateHideShowMenu();
        }

        private void ToggleWindowVisibility()
        {
            if (this.IsVisible) HideWindow(); else ShowWindow();
        }

        private void ToggleAlwaysOnTop(object sender, EventArgs e)
        {
            this.Topmost = !this.Topmost;

            // Update tray icon text
            if (notifyIcon != null)
            {
                notifyIcon.Text = this.Topmost ? "Network Speed Meter (Always on Top)" : "Network Speed Meter";
            }

            // Update checkmark in menu
            UpdateAlwaysOnTopCheckmark();
        }

        private void UpdateAlwaysOnTopCheckmark()
        {
            if (notifyIcon?.ContextMenuStrip == null) return;

            foreach (System.Windows.Forms.ToolStripItem item in notifyIcon.ContextMenuStrip.Items)
            {
                if (item is System.Windows.Forms.ToolStripMenuItem menuItem && menuItem.Text == "Always on Top")
                {
                    menuItem.Checked = this.Topmost;
                    break;
                }
            }
        }

        private void ToggleStartup(object sender, EventArgs e)
        {
            try
            {
                string appName = "SpeedMeterApp";

                // Get the EXE file path
                string executablePath = GetExecutablePath();

                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (key.GetValue(appName) == null)
                        {
                            // Register with EXE path
                            key.SetValue(appName, $"\"{executablePath}\"");
                            System.Windows.MessageBox.Show($"Added to Windows startup:\n{executablePath}", "Startup Settings",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            key.DeleteValue(appName);
                            System.Windows.MessageBox.Show("Removed from Windows startup.", "Startup Settings",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        // Update checkmark in menu
                        UpdateStartupCheckmark();

                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error managing startup: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateStartupCheckmark()
        {
            if (notifyIcon?.ContextMenuStrip == null) return;

            foreach (System.Windows.Forms.ToolStripItem item in notifyIcon.ContextMenuStrip.Items)
            {
                if (item is System.Windows.Forms.ToolStripMenuItem menuItem && menuItem.Text == "Start with Windows")
                {
                    menuItem.Checked = IsStartupEnabled();
                    break;
                }
            }
        }

        private void UpdateHideShowMenu()
        {
            if (notifyIcon?.ContextMenuStrip == null) return;

            foreach (System.Windows.Forms.ToolStripItem item in notifyIcon.ContextMenuStrip.Items)
            {
                if (item is System.Windows.Forms.ToolStripMenuItem menuItem)
                {
                    if (menuItem.Text == "Hide")
                    {
                        menuItem.Enabled = this.IsVisible;
                    }
                    else if (menuItem.Text == "Show")
                    {
                        menuItem.Enabled = !this.IsVisible;
                    }
                }
            }
        }

        private string GetExecutablePath()
        {
            // Get the current process
            Process currentProcess = Process.GetCurrentProcess();

            // Try to get the main module (EXE file)
            try
            {
                string mainModulePath = currentProcess.MainModule.FileName;
                if (File.Exists(mainModulePath) && mainModulePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return mainModulePath;
                }
            }
            catch
            {
                // Continue to fallback method
            }

            // Fallback: Look for EXE file in application directory
            string assemblyLocation = Assembly.GetExecutingAssembly().Location;
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            string assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

            if (!string.IsNullOrEmpty(assemblyDirectory))
            {
                // Get the process name and look for matching EXE
                string processName = currentProcess.ProcessName;
                string expectedExePath = Path.Combine(assemblyDirectory, processName + ".exe");

                if (File.Exists(expectedExePath))
                {
                    return expectedExePath;
                }

                // Look for any EXE file in the directory
                var exeFiles = Directory.GetFiles(assemblyDirectory, "*.exe");
                if (exeFiles.Length > 0)
                {
                    return exeFiles[0];
                }
            }

            // Last resort: Return assembly location (might be DLL)
            return assemblyLocation;
        }

        private void ChangeBackgroundColor(object sender, EventArgs e)
        {
            try
            {
                var colorDialog = new System.Windows.Forms.ColorDialog();
                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // colorDialog.Color is System.Drawing.Color
                    var dColor = colorDialog.Color;
                    var wpfColor = System.Windows.Media.Color.FromArgb(dColor.A, dColor.R, dColor.G, dColor.B);
                    if (MainBorder != null) MainBorder.Background = new SolidColorBrush(wpfColor);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error changing color: {ex.Message}");
            }
        }

        private void ShowConnectionInfo(object sender, EventArgs e)
        {
            try
            {
                if (networkInterfaces == null) return;

                string info = "Network Interfaces:\n\n";
                foreach (var ni in networkInterfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        info += $"{ni.Name}: {ni.Speed / 1_000_000:N0} Mbps\n";
                    }
                }
                System.Windows.MessageBox.Show(info, "Connection Information");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error getting connection info: {ex.Message}");
            }
        }

        private void RefreshNetwork(object sender, EventArgs e)
        {
            try
            {
                networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
                previousDownload = 0;
                previousUpload = 0;
                System.Windows.MessageBox.Show("Network interfaces refreshed successfully.");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error refreshing network: {ex.Message}");
            }
        }

        private void ExitApplication(object sender, EventArgs e)
        {
            try
            {
                // stop sampling and timers, then clean up
                StopColorSampling();
                speedTimer?.Dispose();
                pingTimer?.Dispose();
                StopHistoryFlushTimer();
                SaveHistoryToCsv(); // optional to force final save

                if (logWriter != null)
                {
                    try { logWriter.Flush(); logWriter.Close(); logWriter.Dispose(); } catch { }
                    logWriter = null;
                }

                if (notifyIcon != null)
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error exiting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            StopMonitoringTimers(); // ADD THIS LINE
            StopColorSampling();
            speedTimer?.Dispose();
            pingTimer?.Dispose();
            logWriter?.Dispose();
            StopHistoryFlushTimer();
            SaveHistoryToCsv(); // optional to force final save

            try { logWriter?.Dispose(); } catch { }
            try { notifyIcon?.Dispose(); } catch { }
            base.OnClosed(e);
        }

        // in MainWindow class (make sure 'using SpeedMeterApp.Models;' is present at top)
        public string GetLogFolder() => logFolder;

        public string GetLastPingDisplay() => lastPingMs >= 0 ? $"{lastPingMs} ms" : "N/A";

        public void SetHistoryRetention(int hours)
        {
            historyRetentionHours = Math.Clamp(hours, 1, 168);
            // Trim immediately
            lock (historyLock) TrimHistoryLocked();
            SaveHistoryToCsv();
        }

        public void SetRefreshInterval(int ms)
        {
            refreshIntervalMs = Math.Max(200, ms);
            speedTimer?.Change(0, refreshIntervalMs);
        }

        public void RestoreDefaultsFromDashboard()
        {
            // re-use your existing restore defaults logic
            RestoreDefaults_Click(this, EventArgs.Empty);
        }

        private void RestoreDefaults_Click(MainWindow mainWindow, EventArgs empty)
        {
            throw new NotImplementedException();
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void TestNotification_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Test Notification button clicked");

            // Test with simple notification first
            ShowSimpleNotification("Test Notification", "Testing the notification system", NotificationType.Info);

            // Then test the full system after 1 second
            DispatcherTimer testTimer = new DispatcherTimer();
            testTimer.Interval = TimeSpan.FromSeconds(1);
            testTimer.Tick += (s, args) =>
            {
                testTimer.Stop();

                // Test each notification type
                ShowPremiumNotification("Network Connected", "Internet connection restored", NotificationType.NetworkConnected);

                // Test another after 2 seconds
                DispatcherTimer testTimer2 = new DispatcherTimer();
                testTimer2.Interval = TimeSpan.FromSeconds(2);
                testTimer2.Tick += (s2, args2) =>
                {
                    testTimer2.Stop();
                    ShowPremiumNotification("Charger Connected", "Device is now charging", NotificationType.PowerConnected);

                    // Test another after 2 seconds
                    DispatcherTimer testTimer3 = new DispatcherTimer();
                    testTimer3.Interval = TimeSpan.FromSeconds(2);
                    testTimer3.Tick += (s3, args3) =>
                    {
                        testTimer3.Stop();
                        ShowPremiumNotification("Battery 20%", "Low battery warning", NotificationType.BatteryLow);
                    };
                    testTimer3.Start();
                };
                testTimer2.Start();
            };
            testTimer.Start();
        }

        // Add these methods after the notification methods
        private void CheckNetworkStatus()
        {
            try
            {
                bool isConnected = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                var now = DateTime.Now;

                // Only check every 5 seconds minimum
                if ((now - _lastNetworkCheck).TotalMilliseconds < 5000)
                    return;

                _lastNetworkCheck = now;

                Debug.WriteLine($"Network check: Connected = {isConnected}, WasConnected = {_wasConnected}");

                if (isConnected != _wasConnected)
                {
                    Debug.WriteLine($"Network status changed! Notifying...");

                    if (isConnected)
                    {
                        ShowPremiumNotification(
                            "Network Connected",
                            "Internet connection restored successfully.",
                            NotificationType.NetworkConnected
                        );
                    }
                    else
                    {
                        ShowPremiumNotification(
                            "Network Disconnected",
                            "Internet connection lost.",
                            NotificationType.NetworkDisconnected
                        );
                    }

                    _wasConnected = isConnected;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Network check error: {ex.Message}");
            }
        }

        private void CheckPowerStatus()
        {
            try
            {
                var powerStatus = System.Windows.Forms.SystemInformation.PowerStatus;
                var now = DateTime.Now;

                // Only check every 5 seconds minimum
                if ((now - _lastBatteryCheck).TotalMilliseconds < 5000)
                    return;

                _lastBatteryCheck = now;

                // Check power line status
                var currentPowerStatus = powerStatus.PowerLineStatus;

                Debug.WriteLine($"Power check: Status = {currentPowerStatus}, LastStatus = {_lastPowerStatus}");

                if (currentPowerStatus != _lastPowerStatus)
                {
                    Debug.WriteLine($"Power status changed! Notifying...");

                    if (currentPowerStatus == System.Windows.Forms.PowerLineStatus.Online)
                    {
                        ShowPremiumNotification(
                            "Charger Connected",
                            "Device is now charging.",
                            NotificationType.PowerConnected
                        );
                    }
                    else if (currentPowerStatus == System.Windows.Forms.PowerLineStatus.Offline)
                    {
                        ShowPremiumNotification(
                            "Charger Disconnected",
                            "Device is now running on battery power.",
                            NotificationType.PowerDisconnected
                        );
                    }

                    _lastPowerStatus = currentPowerStatus;
                }

                // Check battery percentage for notifications
                CheckBatteryPercentage(powerStatus);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Power check error: {ex.Message}");
            }
        }

        private void CheckBatteryPercentage(System.Windows.Forms.PowerStatus powerStatus)
        {
            int batteryPercentage = (int)(powerStatus.BatteryLifePercent * 100);
            var currentPowerStatus = powerStatus.PowerLineStatus;

            Debug.WriteLine($"Battery check: {batteryPercentage}%, Power: {currentPowerStatus}, Last: {_lastBatteryPercentage}%");

            // Only show battery notifications when not charging
            if (currentPowerStatus == System.Windows.Forms.PowerLineStatus.Offline)
            {
                // Simplified logic - notify at key thresholds
                if (batteryPercentage <= 20 && _lastBatteryPercentage > 20)
                {
                    Debug.WriteLine($"Battery low at {batteryPercentage}%! Notifying...");
                    ShowPremiumNotification($"Battery {batteryPercentage}%",
                        "Battery is low! Connect charger soon.", NotificationType.BatteryLow);
                }
                else if (batteryPercentage <= 10 && _lastBatteryPercentage > 10)
                {
                    Debug.WriteLine($"Battery critical at {batteryPercentage}%! Notifying...");
                    ShowPremiumNotification($"Battery {batteryPercentage}%",
                        "Battery critically low! Connect charger immediately!", NotificationType.BatteryCritical);
                }
                else if (batteryPercentage <= 5 && _lastBatteryPercentage > 5)
                {
                    Debug.WriteLine($"Battery extremely low at {batteryPercentage}%! Notifying...");
                    ShowPremiumNotification($"Battery {batteryPercentage}%",
                        "Battery extremely low! System may shutdown soon!", NotificationType.BatteryCritical);
                }
            }

            _lastBatteryPercentage = batteryPercentage;
        }

        // Add this method
        private void StartMonitoringTimers()
        {
            try
            {
                Debug.WriteLine("Starting monitoring timers...");

                _monitoringTimer = new System.Windows.Forms.Timer();
                _monitoringTimer.Interval = 2000; // Check every 2 seconds (more reasonable)
                _monitoringTimer.Tick += (s, e) =>
                {
                    try
                    {
                        // Check network status
                        CheckNetworkStatus();

                        // Check power status (includes battery)
                        CheckPowerStatus();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Monitoring tick error: {ex.Message}");
                    }
                };

                _monitoringTimer.Start();

                // Initial checks after a short delay
                DispatcherTimer initTimer = new DispatcherTimer();
                initTimer.Interval = TimeSpan.FromSeconds(1);
                initTimer.Tick += (s, e) =>
                {
                    initTimer.Stop();

                    Debug.WriteLine("Starting initial checks...");
                    CheckNetworkStatus();
                    CheckPowerStatus();

                    Debug.WriteLine("Monitoring timers started successfully!");
                };
                initTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Monitoring timer error: {ex.Message}");
            }
        }

        // Add this method too
        private void StopMonitoringTimers()
        {
            try
            {
                Debug.WriteLine("Stopping monitoring timers...");
                _monitoringTimer?.Stop();
                _monitoringTimer?.Dispose();
                _monitoringTimer = null;
                Debug.WriteLine("Monitoring timers stopped.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Stop monitoring error: {ex.Message}");
            }
        }

        // Small UI handlers (buttons from XAML if present)
        // Context menu handler to toggle debug overlay
        private void ToggleDebug_Click(object sender, RoutedEventArgs e)
        {
            debugEnabled = !debugEnabled;
            if (TxtDebugOverlay != null)
            {
                TxtDebugOverlay.Visibility = debugEnabled ? Visibility.Visible : Visibility.Collapsed;
                if (!debugEnabled)
                {
                    TxtDebugOverlay.Text = string.Empty;
                }
            }
            if (debugEnabled) SampleAndApplyColor();
        }

        private void BtnTheme_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("Theme changer coming soon!");
        }

        private void BtnLog_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("Opening speed log...");
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("Settings panel coming soon!");
        }

        // Toggle CSV logging
        private void ToggleLogging()
        {
            isLogging = !isLogging;

            if (!isLogging)
            {
                try
                {
                    logWriter?.Flush();
                    logWriter?.Close();
                    logWriter = null;
                    System.Windows.MessageBox.Show($"Logging stopped. File saved: {logFileName}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error closing log: {ex.Message}");
                }
            }
            else
            {
                try
                {
                    if (logWriter == null)
                    {
                        logWriter = new StreamWriter(logFileName, true);
                        logWriter.WriteLine("Timestamp,Download_KBps,Upload_KBps,Ping_ms,TotalDownloadBytes,TotalUploadBytes");
                        logWriter.Flush();
                        System.Windows.MessageBox.Show($"Logging started. File: {logFileName}");
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Could not start logging: {ex.Message}");
                    isLogging = false;
                }
            }
        }

        private void ShowTotals()
        {
            try
            {
                string totals = $"Total Download: {FormatTotal(totalDownloadBytes)}\nTotal Upload: {FormatTotal(totalUploadBytes)}\nPing: {(lastPingMs >= 0 ? lastPingMs + " ms" : "N/A")}";
                System.Windows.MessageBox.Show(totals, "Usage Totals");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowTotals error: {ex.Message}");
            }
        }

        private async void TriggerPingNow()
        {
            try
            {
                using (var p = new Ping())
                {
                    var reply = await p.SendPingAsync(pingHost, 2000).ConfigureAwait(false);
                    lastPingMs = reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
                }

                if (notifyIcon != null)
                {
                    notifyIcon.ShowBalloonTip(2000, "Ping Result", lastPingMs >= 0 ? $"Ping {pingHost}: {lastPingMs} ms" : $"Ping {pingHost}: N/A", System.Windows.Forms.ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TriggerPingNow error: {ex.Message}");
                if (notifyIcon != null)
                {
                    notifyIcon.ShowBalloonTip(2000, "Ping Error", ex.Message, System.Windows.Forms.ToolTipIcon.Error);
                }
            }
        }

        private void MainBorder_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            try { this.DragMove(); } catch { }
        }

        // ------- Context/frame menu handlers -------
        // Change font family (quick input)
        private void ChangeFontFamily_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Uses VisualBasic InputBox for quick input (Add reference to Microsoft.VisualBasic in project if missing)
                var input = Microsoft.VisualBasic.Interaction.InputBox("Enter font family (e.g. Segoe UI, Arial):", "Change Font Family", (TxtDownload?.FontFamily?.Source) ?? "Segoe UI");
                if (!string.IsNullOrWhiteSpace(input))
                {
                    var ff = new System.Windows.Media.FontFamily(input);
                    if (TxtDownload != null) TxtDownload.FontFamily = ff;
                    if (TxtUpload != null) TxtUpload.FontFamily = ff;
                    if (TxtTime != null) TxtTime.FontFamily = ff;
                    if (TxtPing != null) TxtPing.FontFamily = ff;
                    if (TxtTotal != null) TxtTotal.FontFamily = ff;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChangeFontFamily error: {ex.Message}");
                System.Windows.MessageBox.Show($"Could not change font family: {ex.Message}");
            }
        }

        // Change font size (MenuItem.Tag contains size as string)
        private void ChangeFontSize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem mi && mi.Tag != null && int.TryParse(mi.Tag.ToString(), out int newSize))
                {
                    if (TxtDownload != null) TxtDownload.FontSize = newSize;
                    if (TxtUpload != null) TxtUpload.FontSize = newSize;
                    if (TxtTime != null) TxtTime.FontSize = Math.Max(10, newSize - 4);
                    if (TxtPing != null) TxtPing.FontSize = Math.Max(10, newSize - 4);
                    if (TxtTotal != null) TxtTotal.FontSize = Math.Max(9, newSize - 6);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChangeFontSize error: {ex.Message}");
            }
        }

        // Change font color using WinForms ColorDialog
        private void ChangeFontColor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new System.Windows.Forms.ColorDialog();
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var c = dlg.Color;
                    var wc = System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B);
                    if (TxtDownload != null) TxtDownload.Foreground = new SolidColorBrush(wc);
                    if (TxtUpload != null) TxtUpload.Foreground = new SolidColorBrush(wc);
                    if (TxtTime != null) TxtTime.Foreground = new SolidColorBrush(wc);
                    if (TxtPing != null) TxtPing.Foreground = new SolidColorBrush(wc);
                    if (TxtTotal != null) TxtTotal.Foreground = new SolidColorBrush(wc);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChangeFontColor error: {ex.Message}");
            }
        }

        // Units selection (MenuItem.Tag = "KB" or "MB")
        private void Units_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem mi && mi.Tag is string tag && (tag == "KB" || tag == "MB"))
                {
                    displayUnits = tag;
                    ThreadPool.QueueUserWorkItem(_ => UpdateNetworkSpeeds(null!));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Units_Click error: {ex.Message}");
            }
        }

        // Change refresh interval (MenuItem.Tag contains ms)
        private void RefreshInterval_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem mi && mi.Tag != null && int.TryParse(mi.Tag.ToString(), out int ms))
                {
                    refreshIntervalMs = ms;
                    speedTimer?.Change(0, refreshIntervalMs);
                    System.Windows.MessageBox.Show($"Refresh interval changed to {ms} ms.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RefreshInterval_Click error: {ex.Message}");
            }
        }

        private void ShowConnectionInfoFromMenu_Click(object sender, RoutedEventArgs e) => ShowConnectionInfo(sender, EventArgs.Empty);
        private void RefreshNetworkFromMenu_Click(object sender, RoutedEventArgs e) => RefreshNetwork(sender, EventArgs.Empty);

        private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainBorder != null) MainBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A));
                if (TxtDownload != null) TxtDownload.FontFamily = new System.Windows.Media.FontFamily("Segoe UI Semibold");
                if (TxtUpload != null) TxtUpload.FontFamily = new System.Windows.Media.FontFamily("Segoe UI Semibold");
                if (TxtTime != null) TxtTime.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
                if (TxtPing != null) TxtPing.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
                if (TxtTotal != null) TxtTotal.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");

                if (TxtDownload != null) TxtDownload.FontSize = 16;
                if (TxtUpload != null) TxtUpload.FontSize = 16;
                if (TxtTime != null) TxtTime.FontSize = 12;
                if (TxtPing != null) TxtPing.FontSize = 12;
                if (TxtTotal != null) TxtTotal.FontSize = 11;

                if (TxtDownload != null) TxtDownload.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4F, 0xC3, 0xF7));
                if (TxtUpload != null) TxtUpload.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x81, 0xC7, 0x84));
                if (TxtTime != null) TxtTime.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99));
                if (TxtPing != null) TxtPing.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99));
                if (TxtTotal != null) TxtTotal.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99));

                displayUnits = "KB";
                refreshIntervalMs = 1000;
                speedTimer?.Change(0, refreshIntervalMs);

                System.Windows.MessageBox.Show("Defaults restored.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RestoreDefaults error: {ex.Message}");
            }
        }
    }
}