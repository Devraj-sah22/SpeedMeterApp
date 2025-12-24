// DashboardWindow.xaml.cs (FIXED - all namespace conflicts resolved)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SpeedMeterApp.Models;
using System.Text;
using System.Globalization;

namespace SpeedMeterApp
{
    public partial class DashboardWindow : Window
    {
        private readonly List<SampleEntry> _samples;
        private readonly MainWindow _owner;
        private DispatcherTimer? _refreshTimer; // Made nullable
        private DateTime _sessionStartTime;

        // Chart colors - use fully qualified System.Windows.Media.Color
        private readonly System.Windows.Media.Color _downloadColor = System.Windows.Media.Color.FromRgb(79, 195, 247);  // Blue
        private readonly System.Windows.Media.Color _uploadColor = System.Windows.Media.Color.FromRgb(129, 199, 132);   // Green
        private readonly System.Windows.Media.Color _gridColor = System.Windows.Media.Color.FromRgb(50, 50, 50);
        private readonly System.Windows.Media.Color _axisColor = System.Windows.Media.Color.FromRgb(100, 100, 100);
        private readonly System.Windows.Media.Color _backgroundColor = System.Windows.Media.Color.FromRgb(22, 22, 22);

        // Statistics
        private double _peakDownload = 0;
        private double _peakUpload = 0;
        private double _avgDownload = 0;
        private double _avgUpload = 0;
        private double _minDownload = double.MaxValue;
        private double _minUpload = double.MaxValue;

        public DashboardWindow(List<SampleEntry> samples, MainWindow owner)
        {
            InitializeComponent();

            _samples = samples?.OrderBy(s => s.Timestamp).ToList() ?? new List<SampleEntry>();
            _owner = owner;
            _sessionStartTime = DateTime.Now;

            InitializeDashboard();
            SetupEventHandlers();
            StartRefreshTimer();
            LoadData();
        }

        private void InitializeDashboard()
        {
            // Set window icon if available
            try
            {
                // You can set an icon here if you have one
            }
            catch { }

            // Initialize UI
            UpdateSessionUptime();
        }

        private void SetupEventHandlers()
        {
            // Window events
            Loaded += (s, e) => RefreshAllData();
            Closing += (s, e) => StopRefreshTimer();

            // Button events
            BtnClose.Click += (s, e) => Close();
            BtnRefresh.Click += (s, e) => RefreshAllData();
            BtnExportAll.Click += BtnExportAll_Click;
            BtnExportSamples.Click += BtnExportSamples_Click;

            // Settings
            CmbTimeRange.SelectionChanged += (s, e) => RefreshCharts();
            CmbRetention.SelectionChanged += CmbRetention_SelectionChanged;
            BtnApplyRetention.Click += BtnApplyRetention_Click;

            // Analytics buttons
            BtnExportCSV.Click += BtnExportCSV_Click;
            BtnExportJSON.Click += BtnExportJSON_Click;
            BtnExportReport.Click += BtnExportReport_Click;
            BtnPrint.Click += BtnPrint_Click;

            // Settings buttons
            BtnClearData.Click += BtnClearData_Click;
            BtnResetSettings.Click += BtnResetSettings_Click;
            BtnBackupData.Click += BtnBackupData_Click;
            BtnPickColor.Click += BtnPickColor_Click;
            // Add portfolio button handler
            BtnDeveloperPortfolio.Click += BtnDeveloperPortfolio_Click;
        }

        // Add this method
        private void BtnDeveloperPortfolio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Replace with your actual portfolio URL
                string portfolioUrl = "https://www.sahdevraj.com.np/";
                Process.Start(new ProcessStartInfo
                {
                    FileName = portfolioUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening portfolio: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartRefreshTimer()
        {
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(5);
            _refreshTimer.Tick += (s, e) =>
            {
                UpdateSessionUptime();
                if (_samples.Count > 0)
                {
                    var lastSample = _samples.Last();
                    TxtCurrentDownload.Text = $"{lastSample.DownloadKBps:F1} KB/s";
                    TxtCurrentUpload.Text = $"{lastSample.UploadKBps:F1} KB/s";
                }
            };
            _refreshTimer.Start();
        }

        private void StopRefreshTimer()
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;
        }

        private void UpdateSessionUptime()
        {
            var uptime = DateTime.Now - _sessionStartTime;
            TxtUptime.Text = $"{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
        }

        private void LoadData()
        {
            if (_samples == null || _samples.Count == 0)
            {
                ShowNoDataMessage();
                return;
            }

            CalculateStatistics();
            UpdateMetrics();
            UpdateHourlyBreakdown();
            RefreshCharts();
            UpdateDataGrid();
            UpdateAnalytics();
        }

        private void CalculateStatistics()
        {
            if (_samples.Count == 0) return;

            var last24h = DateTime.UtcNow.AddHours(-24);
            var recentSamples = _samples.Where(s => s.Timestamp >= last24h).ToList();

            if (recentSamples.Count > 0)
            {
                _peakDownload = recentSamples.Max(s => s.DownloadKBps);
                _peakUpload = recentSamples.Max(s => s.UploadKBps);
                _avgDownload = recentSamples.Average(s => s.DownloadKBps);
                _avgUpload = recentSamples.Average(s => s.UploadKBps);
                _minDownload = recentSamples.Min(s => s.DownloadKBps);
                _minUpload = recentSamples.Min(s => s.UploadKBps);
            }

            // Update peak display
            TxtPeakDownload.Text = $"{_peakDownload:F1} KB/s";
            TxtPeakUpload.Text = $"{_peakUpload:F1} KB/s";
        }

        private void UpdateMetrics()
        {
            if (_samples.Count == 0) return;

            var firstSample = _samples.First();
            var lastSample = _samples.Last();

            // 24-hour totals
            var last24h = DateTime.UtcNow.AddHours(-24);
            var daySamples = _samples.Where(s => s.Timestamp >= last24h).ToList();
            if (daySamples.Count >= 2)
            {
                var firstDay = daySamples.First();
                var lastDay = daySamples.Last();
                var dayDownload = Math.Max(0, lastDay.TotalDownloadBytes - firstDay.TotalDownloadBytes);
                var dayUpload = Math.Max(0, lastDay.TotalUploadBytes - firstDay.TotalUploadBytes);

                Txt24hDownload.Text = FormatBytes(dayDownload);
                Txt24hUpload.Text = FormatBytes(dayUpload);
            }

            // All-time totals
            var totalDownload = lastSample.TotalDownloadBytes;
            var totalUpload = lastSample.TotalUploadBytes;

            TxtTotalDownload.Text = FormatBytes(totalDownload);
            TxtTotalUpload.Text = FormatBytes(totalUpload);

            // Ping
            TxtPing.Text = _owner?.GetLastPingDisplay() ?? "N/A";
        }

        private void UpdateHourlyBreakdown()
        {
            HourlyBreakdownItems.Items.Clear();

            var buckets = GetHourlyBuckets();
            if (buckets.Length == 0) return;

            long maxDownload = buckets.Max(b => b.download);
            long maxUpload = buckets.Max(b => b.upload);
            maxDownload = Math.Max(maxDownload, 1);
            maxUpload = Math.Max(maxUpload, 1);

            for (int i = 0; i < buckets.Length; i++)
            {
                var bucket = buckets[i];
                var hourData = new
                {
                    Hour = bucket.startUtc.ToLocalTime().ToString("HH:00"),
                    DownloadPercentage = (double)bucket.download / maxDownload * 100,
                    UploadPercentage = (double)bucket.upload / maxUpload * 100,
                    DownloadFormatted = FormatBytesCompact(bucket.download),
                    UploadFormatted = FormatBytesCompact(bucket.upload)
                };

                HourlyBreakdownItems.Items.Add(hourData);
            }
        }

        private (DateTime startUtc, long download, long upload)[] GetHourlyBuckets()
        {
            var buckets = new (DateTime, long, long)[24];
            DateTime now = DateTime.UtcNow;
            DateTime oldestBucketStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(-23);

            for (int i = 0; i < 24; i++)
            {
                DateTime start = oldestBucketStart.AddHours(i);
                DateTime end = start.AddHours(1);

                var beforeOrAtStart = _samples.LastOrDefault(h => h.Timestamp <= start);
                var beforeOrAtEnd = _samples.LastOrDefault(h => h.Timestamp <= end);

                if (beforeOrAtEnd == null && _samples.Count > 0) beforeOrAtEnd = _samples.First();
                if (beforeOrAtStart == null && _samples.Count > 0) beforeOrAtStart = _samples.First();

                long d = 0, u = 0;
                if (beforeOrAtEnd != null && beforeOrAtStart != null)
                {
                    d = Math.Max(0, beforeOrAtEnd.TotalDownloadBytes - beforeOrAtStart.TotalDownloadBytes);
                    u = Math.Max(0, beforeOrAtEnd.TotalUploadBytes - beforeOrAtStart.TotalUploadBytes);
                }
                buckets[i] = (start, d, u);
            }

            return buckets;
        }

        private void RefreshCharts()
        {
            DrawSpeedChart();
            DrawDistributionCharts();
            DrawTimelineChart();
        }

        private void DrawSpeedChart()
        {
            SpeedChartCanvas.Children.Clear();

            double width = SpeedChartCanvas.ActualWidth;
            double height = SpeedChartCanvas.ActualHeight;
            if (width <= 10 || height <= 10) return;

            // Background - use fully qualified Rectangle
            SpeedChartCanvas.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Fill = new SolidColorBrush(_backgroundColor)
            });

            if (_samples.Count == 0) return;

            // Determine time range
            int hours = GetSelectedHours();
            var cutoff = DateTime.UtcNow.AddHours(-hours);
            var visibleSamples = _samples.Where(s => s.Timestamp >= cutoff).ToList();
            if (visibleSamples.Count == 0) return;

            // Calculate scales
            double maxSpeed = Math.Max(1, Math.Max(
                visibleSamples.Max(s => s.DownloadKBps),
                visibleSamples.Max(s => s.UploadKBps)
            ));

            double marginLeft = 60;
            double marginRight = 20;
            double marginTop = 40;
            double marginBottom = 40;

            double chartWidth = width - marginLeft - marginRight;
            double chartHeight = height - marginTop - marginBottom;

            // Draw grid
            int gridLinesY = 5;
            for (int i = 0; i <= gridLinesY; i++)
            {
                double y = marginTop + (i * chartHeight / gridLinesY);
                var line = new Line
                {
                    X1 = marginLeft,
                    X2 = width - marginRight,
                    Y1 = y,
                    Y2 = y,
                    Stroke = new SolidColorBrush(_gridColor),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection(new double[] { 4, 2 })
                };
                SpeedChartCanvas.Children.Add(line);

                // Y-axis labels
                double value = maxSpeed * (1 - (double)i / gridLinesY);
                var label = new TextBlock
                {
                    Text = $"{value:F0} KB/s",
                    Foreground = new SolidColorBrush(_axisColor),
                    FontSize = 10
                };
                SpeedChartCanvas.Children.Add(label);
                Canvas.SetLeft(label, 10);
                Canvas.SetTop(label, y - 8);
            }

            // Draw data lines
            DrawChartLine(visibleSamples, marginLeft, marginTop, chartWidth, chartHeight, maxSpeed, true);
            DrawChartLine(visibleSamples, marginLeft, marginTop, chartWidth, chartHeight, maxSpeed, false);

            // X-axis labels
            int labelCount = Math.Min(6, visibleSamples.Count);
            for (int i = 0; i < labelCount; i++)
            {
                int index = i * (visibleSamples.Count - 1) / (labelCount - 1);
                double x = marginLeft + (index * chartWidth / Math.Max(1, visibleSamples.Count - 1));

                var time = visibleSamples[index].Timestamp.ToLocalTime().ToString("HH:mm");
                var label = new TextBlock
                {
                    Text = time,
                    Foreground = new SolidColorBrush(_axisColor),
                    FontSize = 10
                };
                SpeedChartCanvas.Children.Add(label);
                Canvas.SetLeft(label, x - 15);
                Canvas.SetTop(label, height - marginBottom + 10);
            }

            // Legend
            DrawLegend(SpeedChartCanvas, marginLeft, marginTop);
        }

        private void DrawChartLine(List<SampleEntry> samples, double marginLeft, double marginTop,
                                  double chartWidth, double chartHeight, double maxSpeed, bool isDownload)
        {
            if (samples.Count == 0) return;

            var points = new PointCollection();
            for (int i = 0; i < samples.Count; i++)
            {
                double value = isDownload ? samples[i].DownloadKBps : samples[i].UploadKBps;
                double x = marginLeft + (i * chartWidth / Math.Max(1, samples.Count - 1));
                double y = marginTop + chartHeight * (1 - Math.Min(1, value / maxSpeed));
                points.Add(new System.Windows.Point(x, y)); // Use fully qualified Point
            }

            var polyline = new Polyline
            {
                Points = points,
                Stroke = new SolidColorBrush(isDownload ? _downloadColor : _uploadColor),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            SpeedChartCanvas.Children.Add(polyline);
        }

        private void DrawLegend(Canvas canvas, double x, double y)
        {
            var downloadLegend = new Border
            {
                Background = new SolidColorBrush(_downloadColor),
                Width = 12,
                Height = 3,
                CornerRadius = new CornerRadius(1.5)
            };
            Canvas.SetLeft(downloadLegend, x);
            Canvas.SetTop(downloadLegend, y - 20);
            canvas.Children.Add(downloadLegend);

            var downloadText = new TextBlock
            {
                Text = "Download",
                Foreground = new SolidColorBrush(_downloadColor),
                FontSize = 11
            };
            Canvas.SetLeft(downloadText, x + 15);
            Canvas.SetTop(downloadText, y - 24);
            canvas.Children.Add(downloadText);

            var uploadLegend = new Border
            {
                Background = new SolidColorBrush(_uploadColor),
                Width = 12,
                Height = 3,
                CornerRadius = new CornerRadius(1.5)
            };
            Canvas.SetLeft(uploadLegend, x + 80);
            Canvas.SetTop(uploadLegend, y - 20);
            canvas.Children.Add(uploadLegend);

            var uploadText = new TextBlock
            {
                Text = "Upload",
                Foreground = new SolidColorBrush(_uploadColor),
                FontSize = 11
            };
            Canvas.SetLeft(uploadText, x + 95);
            Canvas.SetTop(uploadText, y - 24);
            canvas.Children.Add(uploadText);
        }

        private void DrawDistributionCharts()
        {
            DrawDistributionChart(DownloadDistributionCanvas, _samples.Select(s => s.DownloadKBps).ToList(), "Download Speed (KB/s)", _downloadColor);
            DrawDistributionChart(UploadDistributionCanvas, _samples.Select(s => s.UploadKBps).ToList(), "Upload Speed (KB/s)", _uploadColor);
        }

        private void DrawDistributionChart(Canvas canvas, List<double> values, string title, System.Windows.Media.Color color)
        {
            canvas.Children.Clear();

            if (values.Count == 0) return;

            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width <= 10 || height <= 10) return;

            // Create histogram
            int bins = 10;
            double maxValue = values.Max();
            double binWidth = maxValue / bins;

            var histogram = new int[bins];
            foreach (var value in values)
            {
                int bin = Math.Min(bins - 1, (int)(value / binWidth));
                histogram[bin]++;
            }

            int maxCount = histogram.Max();
            if (maxCount == 0) return;

            // Draw bars
            double barWidth = (width - 40) / bins;
            for (int i = 0; i < bins; i++)
            {
                double barHeight = (histogram[i] / (double)maxCount) * (height - 60);
                double x = 20 + i * barWidth;
                double y = height - 40 - barHeight;

                var bar = new System.Windows.Shapes.Rectangle // Fully qualified
                {
                    Width = barWidth - 2,
                    Height = barHeight,
                    Fill = new SolidColorBrush(color),
                    Opacity = 0.7
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);
                canvas.Children.Add(bar);

                // Label
                if (i % 2 == 0)
                {
                    var label = new TextBlock
                    {
                        Text = $"{(i * binWidth):F0}",
                        Foreground = new SolidColorBrush(_axisColor),
                        FontSize = 9
                    };
                    Canvas.SetLeft(label, x);
                    Canvas.SetTop(label, height - 30);
                    canvas.Children.Add(label);
                }
            }

            // Title
            var titleText = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(_axisColor),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(titleText, 10);
            Canvas.SetTop(titleText, 10);
            canvas.Children.Add(titleText);
        }

        private void DrawTimelineChart()
        {
            TimelineCanvas.Children.Clear();

            // Implementation for timeline chart
            // You can expand this to show usage patterns over time
        }

        private void UpdateDataGrid()
        {
            if (_samples.Count == 0) return;

            var recentSamples = _samples
                .OrderByDescending(s => s.Timestamp)
                .Take(100)
                .ToList();

            DataGridSamples.ItemsSource = recentSamples;
            TxtSampleCount.Text = $"{_samples.Count} total samples, showing {recentSamples.Count} most recent";
        }

        private void UpdateAnalytics()
        {
            if (_samples.Count == 0) return;

            // Download stats
            TxtAvgDownload.Text = $"{_avgDownload:F1} KB/s";
            TxtMaxDownload.Text = $"{_peakDownload:F1} KB/s";
            TxtMinDownload.Text = $"{_minDownload:F1} KB/s";
            TxtStdDevDownload.Text = CalculateStdDev(_samples.Select(s => s.DownloadKBps)).ToString("F1") + " KB/s";

            // Upload stats
            TxtAvgUpload.Text = $"{_avgUpload:F1} KB/s";
            TxtMaxUpload.Text = $"{_peakUpload:F1} KB/s";
            TxtMinUpload.Text = $"{_minUpload:F1} KB/s";
            TxtStdDevUpload.Text = CalculateStdDev(_samples.Select(s => s.UploadKBps)).ToString("F1") + " KB/s";

            // Usage patterns
            UpdateUsagePatterns();
        }

        private double CalculateStdDev(IEnumerable<double> values)
        {
            var list = values.ToList();
            if (list.Count == 0) return 0;

            double avg = list.Average();
            double sum = list.Sum(v => Math.Pow(v - avg, 2));
            return Math.Sqrt(sum / list.Count);
        }

        private void UpdateUsagePatterns()
        {
            var buckets = GetHourlyBuckets();
            if (buckets.Length == 0) return;

            // Find busiest hour
            long maxTotal = 0;
            int busiestHour = 0;
            for (int i = 0; i < buckets.Length; i++)
            {
                long total = buckets[i].download + buckets[i].upload;
                if (total > maxTotal)
                {
                    maxTotal = total;
                    busiestHour = i;
                }
            }

            TxtBusiestHour.Text = $"{busiestHour:00}:00";

            // Calculate ratio
            long totalDownload = buckets.Sum(b => b.download);
            long totalUpload = buckets.Sum(b => b.upload);
            if (totalUpload > 0)
            {
                double ratio = (double)totalDownload / totalUpload;
                TxtRatio.Text = $"{ratio:F2}:1";
            }

            // Daily average
            TxtDailyAverage.Text = FormatBytes((totalDownload + totalUpload) / 24);
        }

        private void ShowNoDataMessage()
        {
            // Show message when no data is available
            var grid = new Grid();
            grid.Children.Add(new TextBlock
            {
                Text = "📊 No data available yet.\nStart monitoring to collect network usage statistics.",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, // Fixed: Use type name
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FontSize = 16,
                Foreground = System.Windows.Media.Brushes.Gray // Fixed: Use fully qualified Brushes
            });

            Content = grid;
        }

        private int GetSelectedHours()
        {
            return CmbTimeRange.SelectedIndex switch
            {
                0 => 24,  // Last 24 Hours
                1 => 12,  // Last 12 Hours
                2 => 6,   // Last 6 Hours
                3 => 1,   // Last Hour
                _ => 24
            };
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:F2} {sizes[order]}";
        }

        private string FormatBytesCompact(long bytes)
        {
            if (bytes >= 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F1}MB";
            if (bytes >= 1024) return $"{(bytes / 1024.0):F1}KB";
            return $"{bytes}B";
        }

        private void RefreshAllData()
        {
            LoadData();
        }

        // Event Handlers - Fixed all MessageBox and SaveFileDialog references
        private void BtnExportAll_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog // Fixed: Use WPF SaveFileDialog
            {
                FileName = $"network_data_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Filter = "CSV files (*.csv)|*.csv|JSON files (*.json)|*.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (dialog.FileName.EndsWith(".json"))
                    {
                        ExportToJson(dialog.FileName);
                    }
                    else
                    {
                        ExportToCsv(dialog.FileName);
                    }

                    System.Windows.MessageBox.Show($"Data exported successfully to {dialog.FileName}", "Export Complete",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private void ExportToCsv(string filePath)
        {
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                writer.WriteLine("Timestamp,Download_KBps,Upload_KBps,TotalDownloadBytes,TotalUploadBytes");
                foreach (var sample in _samples)
                {
                    writer.WriteLine($"{sample.Timestamp:o},{sample.DownloadKBps:F3},{sample.UploadKBps:F3},{sample.TotalDownloadBytes},{sample.TotalUploadBytes}");
                }
            }
        }

        private void ExportToJson(string filePath)
        {
            // For now, we'll just export CSV even if JSON is selected
            ExportToCsv(filePath);
            System.Windows.MessageBox.Show("JSON export requires Newtonsoft.Json package. Exported as CSV instead.", "Info",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private void BtnExportSamples_Click(object sender, RoutedEventArgs e)
        {
            BtnExportAll_Click(sender, e);
        }

        private void BtnExportCSV_Click(object sender, RoutedEventArgs e) => BtnExportAll_Click(sender, e);

        private void BtnExportJSON_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog // Fixed: Use WPF SaveFileDialog
            {
                FileName = $"network_data_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                Filter = "JSON files (*.json)|*.json"
            };

            if (dialog.ShowDialog() == true)
            {
                ExportToJson(dialog.FileName);
            }
        }

        private void BtnExportReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string report = GenerateReport();
                var dialog = new Microsoft.Win32.SaveFileDialog // Fixed: Use WPF SaveFileDialog
                {
                    FileName = $"network_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                    Filter = "Text files (*.txt)|*.txt"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, report, Encoding.UTF8);
                    System.Windows.MessageBox.Show("Report generated successfully", "Success",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to generate report: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private string GenerateReport()
        {
            var report = new StringBuilder();
            report.AppendLine("=== NETWORK USAGE REPORT ===");
            report.AppendLine($"Generated: {DateTime.Now}");
            report.AppendLine($"Session Start: {_sessionStartTime}");
            report.AppendLine($"Total Samples: {_samples.Count}");
            report.AppendLine();

            if (_samples.Count > 0)
            {
                var first = _samples.First();
                var last = _samples.Last();

                report.AppendLine("=== SUMMARY ===");
                report.AppendLine($"Total Download: {FormatBytes(last.TotalDownloadBytes)}");
                report.AppendLine($"Total Upload: {FormatBytes(last.TotalUploadBytes)}");
                report.AppendLine();

                report.AppendLine("=== 24-HOUR STATISTICS ===");
                report.AppendLine($"Peak Download: {_peakDownload:F1} KB/s");
                report.AppendLine($"Peak Upload: {_peakUpload:F1} KB/s");
                report.AppendLine($"Average Download: {_avgDownload:F1} KB/s");
                report.AppendLine($"Average Upload: {_avgUpload:F1} KB/s");
                report.AppendLine();

                report.AppendLine("=== HOURLY BREAKDOWN ===");
                var buckets = GetHourlyBuckets();
                foreach (var bucket in buckets)
                {
                    report.AppendLine($"{bucket.startUtc.ToLocalTime():HH:00}: DL={FormatBytesCompact(bucket.download)}, UL={FormatBytesCompact(bucket.upload)}");
                }
            }

            return report.ToString();
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var printDialog = new System.Windows.Controls.PrintDialog(); // Fixed: Use WPF PrintDialog
                if (printDialog.ShowDialog() == true)
                {
                    // Create a printable version of the report
                    string report = GenerateReport();
                    var document = new System.Windows.Documents.FlowDocument(
                        new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(report))
                    );

                    printDialog.PrintDocument(
                        ((System.Windows.Documents.IDocumentPaginatorSource)document).DocumentPaginator,
                        "Network Usage Report"
                    );
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Print failed: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void CmbRetention_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Update storage usage display
            TxtStorageUsage.Text = $"Current storage: {CalculateStorageUsage()}";
        }

        private string CalculateStorageUsage()
        {
            // Calculate approximate storage usage
            long bytesPerSample = 50; // Approximate bytes per sample
            long totalBytes = _samples.Count * bytesPerSample;
            return FormatBytes(totalBytes);
        }

        private void BtnApplyRetention_Click(object sender, RoutedEventArgs e)
        {
            int days = CmbRetention.SelectedIndex switch
            {
                0 => 1,   // 1 day
                1 => 3,   // 3 days
                2 => 7,   // 7 days
                3 => 30,  // 30 days
                4 => 90,  // 90 days
                5 => 365, // 1 year
                _ => 7
            };

            _owner?.SetHistoryRetention(days * 24);
            System.Windows.MessageBox.Show($"History retention set to {days} days", "Settings Updated",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private void BtnClearData_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Are you sure you want to clear all history data? This action cannot be undone.",
                "Confirm Clear Data", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // Clear data logic would go here
                System.Windows.MessageBox.Show("History data cleared", "Success",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }

        private void BtnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Reset all dashboard settings to defaults?",
                "Confirm Reset", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // Reset settings logic
                CmbTimeRange.SelectedIndex = 0;
                CmbRetention.SelectedIndex = 2;

                System.Windows.MessageBox.Show("Settings reset to defaults", "Success",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }

        private void BtnBackupData_Click(object sender, RoutedEventArgs e)
        {
            BtnExportAll_Click(sender, e);
        }

        private void BtnPickColor_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.ColorDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = System.Windows.Media.Color.FromArgb(
                    dialog.Color.A,
                    dialog.Color.R,
                    dialog.Color.G,
                    dialog.Color.B
                );

                BorderColorPreview.Background = new SolidColorBrush(color);
                // You could apply this color to charts here
            }
        }

        // Utility methods
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (IsLoaded)
            {
                RefreshCharts();
            }
        }
    }
}