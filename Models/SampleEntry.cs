// SpeedMeterApp/Models/SampleEntry.cs
using System;

namespace SpeedMeterApp.Models
{
    // Simple public model for history samples
    public class SampleEntry
    {
        // stored as UTC
        public DateTime Timestamp { get; set; }

        // cumulative totals (bytes)
        public long TotalDownloadBytes { get; set; }
        public long TotalUploadBytes { get; set; }

        // instantaneous throughput in KB/s
        public double DownloadKBps { get; set; }
        public double UploadKBps { get; set; }
    }
}
