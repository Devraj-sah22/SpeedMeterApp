// HistoryWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SpeedMeterApp.Models;

namespace SpeedMeterApp
{
    public partial class HistoryWindow : Window
    {
        // primary data/state
        private readonly List<SampleEntry> _samples;
        private readonly Window _owner;
        private double _yZoom = 1.0; // zoom multiplier

        // drawing caches / brushes (explicitly use WPF Color)
        private readonly SolidColorBrush _bg = new SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 18, 18));
        private readonly SolidColorBrush _gridBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 40, 40));
        private readonly SolidColorBrush _axisBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100));

        public HistoryWindow(List<SampleEntry> samples, Window owner)
        {
            InitializeComponent();

            _owner = owner;
            Owner = owner;

            // ensure samples is a non-null, ordered list
            _samples = (samples ?? new List<SampleEntry>()).OrderBy(s => s.Timestamp).ToList();

            // wire buttons (names must match HistoryWindow.xaml)
            BtnClose.Click += (s, e) => Close();
            BtnExportCsv.Click += BtnExportCsv_Click; // Fixed from BtnExport

            // set grid data if available
            GridSamples.ItemsSource = _samples.OrderByDescending(s => s.Timestamp).ToList(); // Fixed from HistoryGrid

            // compute 24h totals and summary text
            var (d24, u24) = Get24HourTotal();
            TxtSummary.Text = $"Down: {FormatBytes(d24)}   Up: {FormatBytes(u24)}";

            // chart events - use GraphCanvas (not ChartCanvas)
            GraphCanvas.SizeChanged += (s, e) => Redraw();
            GraphCanvas.MouseMove += ChartCanvas_MouseMove;
            GraphCanvas.MouseLeave += (s, e) => { /* No Hint control in XAML */ };
            GraphCanvas.MouseWheel += GraphCanvas_MouseWheel; // Fixed from ChartCanvas_MouseWheel

            // draw when loaded
            Loaded += (s, e) => Redraw();
        }

        // convenience overload for MainWindow callers
        public HistoryWindow(List<SampleEntry> samples, MainWindow mainWindow)
            : this(samples, (Window)mainWindow)
        {
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e) // Fixed method name
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "speed_history.csv", Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    using (var sw = new System.IO.StreamWriter(dlg.FileName, false))
                    {
                        sw.WriteLine("TimestampUtc,TotalDownloadBytes,TotalUploadBytes,DownloadKBps,UploadKBps");
                        foreach (var s in _samples)
                            sw.WriteLine($"{s.Timestamp:o},{s.TotalDownloadBytes},{s.TotalUploadBytes},{s.DownloadKBps:F3},{s.UploadKBps:F3}");
                    }
                    System.Windows.MessageBox.Show($"Exported to {dlg.FileName}");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Export failed: {ex.Message}");
                }
            }
        }

        private (long download24hBytes, long upload24hBytes) Get24HourTotal()
        {
            if (_samples == null || _samples.Count < 2) return (0, 0);
            // use first and last (already ordered by timestamp ascending)
            var first = _samples.First();
            var last = _samples.Last();
            return (Math.Max(0, last.TotalDownloadBytes - first.TotalDownloadBytes),
                    Math.Max(0, last.TotalUploadBytes - first.TotalUploadBytes));
        }

        private string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            if (bytes >= 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F2} MB";
            if (bytes >= 1024L) return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }

        private void GraphCanvas_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) // Fixed method name
        {
            // Zoom in/out Y axis
            const double factor = 1.15;
            if (e.Delta > 0) _yZoom *= factor; else _yZoom /= factor;
            _yZoom = Math.Clamp(_yZoom, 0.25, 8.0);
            Redraw();
        }

        private void ChartCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Note: The XAML doesn't have Hint controls, so we'll skip this functionality
            // or you can add Hint, HintTime, HintDownload, HintUpload controls to HistoryWindow.xaml
            return;
            
            /* Original code commented out since controls don't exist:
            if (_samples == null || _samples.Count == 0) { Hint.Visibility = Visibility.Collapsed; return; }

            var pos = e.GetPosition(GraphCanvas);
            double w = GraphCanvas.ActualWidth;
            double h = GraphCanvas.ActualHeight;
            if (w <= 10 || h <= 10) return;

            int n = Math.Max(1, _samples.Count);
            double margin = 48; // left/right margin for labels
            double x0 = margin;
            double x1 = w - margin;
            double span = x1 - x0;

            double frac = Math.Clamp((pos.X - x0) / Math.Max(1, span), 0.0, 1.0);
            double rawIndex = frac * (n - 1);
            int idx = (int)Math.Round(rawIndex);
            idx = Math.Clamp(idx, 0, n - 1);
            var s = _samples[idx];

            // determine Y scale using currently drawn scales
            double maxKbps = Math.Max(1.0, _samples.Max(x => Math.Max(Math.Abs(x.DownloadKBps), Math.Abs(x.UploadKBps))));

            // safe compute cx (handle n == 1)
            double cx = (n == 1) ? (x0 + span / 2.0) : (x0 + (idx / (double)(n - 1)) * span);

            double downY = 30 + (h - 60) * (1.0 - Math.Min(1.0, (s.DownloadKBps / maxKbps) * _yZoom));
            double upY = 30 + (h - 60) * (1.0 - Math.Min(1.0, (s.UploadKBps / maxKbps) * _yZoom));
            double hintX = Math.Clamp(cx + 12, 8, w - 180);
            double hintY = Math.Clamp(Math.Min(downY, upY) - 10, 8, h - 60);

            Hint.Visibility = Visibility.Visible;
            Canvas.SetLeft(Hint, hintX);
            Canvas.SetTop(Hint, hintY);
            HintTime.Text = s.Timestamp.ToLocalTime().ToString("g");
            HintDownload.Text = $"Download: {s.DownloadKBps:F2} KB/s";
            HintUpload.Text = $"Upload: {s.UploadKBps:F2} KB/s";
            */
        }

        private void Redraw()
        {
            GraphCanvas.Children.Clear(); // Fixed from ChartCanvas
            double w = GraphCanvas.ActualWidth; // Fixed from ChartCanvas
            double h = GraphCanvas.ActualHeight; // Fixed from ChartCanvas
            if (w <= 10 || h <= 10) return;

            // backdrop (explicit Shapes.Rectangle)
            var rect = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = _bg };
            GraphCanvas.Children.Add(rect); // Fixed from ChartCanvas

            if (_samples == null || _samples.Count == 0)
            {
                var msg = new TextBlock { Text = "No history samples", Foreground = System.Windows.Media.Brushes.Gray, FontSize = 14 };
                Canvas.SetLeft(msg, w / 2 - 60); Canvas.SetTop(msg, h / 2 - 10);
                GraphCanvas.Children.Add(msg); // Fixed from ChartCanvas
                return;
            }

            // margins and scales
            double left = 48, right = w - 48, top = 20, bottom = h - 28;
            double plotW = Math.Max(10, right - left);
            double plotH = Math.Max(10, bottom - top);

            // grid lines (Y)
            int yGridCount = 5;
            for (int i = 0; i <= yGridCount; i++)
            {
                double y = top + i * (plotH / yGridCount);
                var line = new Line { X1 = left, X2 = right, Y1 = y, Y2 = y, Stroke = _gridBrush, StrokeThickness = 1 };
                GraphCanvas.Children.Add(line); // Fixed from ChartCanvas
            }

            // X axis ticks/labels (up to 6 ticks)
            int tickCount = Math.Min(6, _samples.Count);
            for (int i = 0; i < tickCount; i++)
            {
                double fx = (tickCount == 1) ? 0.0 : i / (double)(tickCount - 1);
                double x = left + fx * plotW;
                var tline = new Line { X1 = x, X2 = x, Y1 = bottom, Y2 = bottom + 6, Stroke = _axisBrush, StrokeThickness = 1 };
                GraphCanvas.Children.Add(tline); // Fixed from ChartCanvas

                int sampleIndex = (int)Math.Round(fx * (_samples.Count - 1));
                var time = _samples[sampleIndex].Timestamp.ToLocalTime().ToString("HH:mm");
                var tb = new TextBlock { Text = time, Foreground = System.Windows.Media.Brushes.Gray, FontSize = 11 };
                Canvas.SetLeft(tb, x - 20); Canvas.SetTop(tb, bottom + 8);
                GraphCanvas.Children.Add(tb); // Fixed from ChartCanvas
            }

            // Y scale: use KB/s maximum among samples
            double maxKbps = Math.Max(1.0, _samples.Max(x => Math.Max(Math.Abs(x.DownloadKBps), Math.Abs(x.UploadKBps))));
            double scaledMax = maxKbps * _yZoom;

            // draw Y labels
            for (int i = 0; i <= yGridCount; i++)
            {
                double frac = 1.0 - (i / (double)yGridCount);
                double val = frac * scaledMax;
                var tb = new TextBlock { Text = $"{val:F0} KB/s", Foreground = System.Windows.Media.Brushes.Gray, FontSize = 11 };
                Canvas.SetLeft(tb, 6); Canvas.SetTop(tb, top + i * (plotH / yGridCount) - 8);
                GraphCanvas.Children.Add(tb); // Fixed from ChartCanvas
            }

            // Build polylines for download/upload
            var downloadFigure = new PathFigure();
            var uploadFigure = new PathFigure();
            var downloadSegments = new PolyLineSegment();
            var uploadSegments = new PolyLineSegment();

            int n = _samples.Count;
            for (int i = 0; i < n; i++)
            {
                double fx = (n == 1) ? 0.5 : (i / (double)(n - 1));
                double x = left + fx * plotW;
                double dVal = Math.Min(scaledMax, Math.Max(0, _samples[i].DownloadKBps));
                double uVal = Math.Min(scaledMax, Math.Max(0, _samples[i].UploadKBps));
                double dy = top + (1.0 - (dVal / scaledMax)) * plotH;
                double uy = top + (1.0 - (uVal / scaledMax)) * plotH;

                if (i == 0)
                {
                    downloadFigure.StartPoint = new System.Windows.Point(x, dy);
                    uploadFigure.StartPoint = new System.Windows.Point(x, uy);
                }
                else
                {
                    downloadSegments.Points.Add(new System.Windows.Point(x, dy));
                    uploadSegments.Points.Add(new System.Windows.Point(x, uy));
                }
            }

            downloadFigure.Segments.Add(downloadSegments);
            uploadFigure.Segments.Add(uploadSegments);

            var downloadPath = new Path
            {
                Stroke = System.Windows.Media.Brushes.CornflowerBlue,
                StrokeThickness = 2,
                Data = new PathGeometry(new[] { downloadFigure }),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                SnapsToDevicePixels = true
            };
            var uploadPath = new Path
            {
                Stroke = System.Windows.Media.Brushes.LightGreen,
                StrokeThickness = 2,
                Data = new PathGeometry(new[] { uploadFigure }),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                SnapsToDevicePixels = true
            };

            GraphCanvas.Children.Add(downloadPath); // Fixed from ChartCanvas
            GraphCanvas.Children.Add(uploadPath); // Fixed from ChartCanvas

            // top-right legend
            var legDownload = new TextBlock { Text = "Download", Foreground = System.Windows.Media.Brushes.CornflowerBlue, FontWeight = FontWeights.SemiBold };
            var legUpload = new TextBlock { Text = "Upload", Foreground = System.Windows.Media.Brushes.LightGreen, FontWeight = FontWeights.SemiBold };
            Canvas.SetLeft(legDownload, right - 160); Canvas.SetTop(legDownload, top + 6);
            Canvas.SetLeft(legUpload, right - 80); Canvas.SetTop(legUpload, top + 6);
            GraphCanvas.Children.Add(legDownload); // Fixed from ChartCanvas
            GraphCanvas.Children.Add(legUpload); // Fixed from ChartCanvas
        }
    }
}