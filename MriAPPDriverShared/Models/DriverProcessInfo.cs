using System;

namespace MriAPPDriverShared.Models
{
    /// <summary>
    /// Represents an MriAPPDriver.exe process matched to its report context
    /// from the MRI_Server_Messages table.
    /// </summary>
    public class DriverProcessInfo
    {
        public int ProcessId { get; set; }
        public string? MachineName { get; set; }
        public string? SessionId { get; set; }
        public string? UserId { get; set; }
        public DateTime? StartTime { get; set; }
        public string? ReportName { get; set; }
        public string? Description { get; set; }
        public string? ComputerName { get; set; }

        /// <summary>How long the process has been running as of query time.</summary>
        public TimeSpan? RunDuration => StartTime.HasValue
            ? (TimeSpan?)(DateTime.Now - StartTime.Value)
            : null;
    }
}
