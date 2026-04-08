using System;

namespace MriAPPDriverMonitor
{
    public class MonitorSettings
    {
        /// <summary>How often the monitor polls for stale processes (seconds).</summary>
        public int PollingIntervalSeconds { get; set; } = 60;

        /// <summary>How long an MriAPPDriver.exe must be running before it is killed (minutes).</summary>
        public int ProcessAgeThresholdMinutes { get; set; } = 30;
    }
}
