using System.Diagnostics;
using MriAPPDriverShared.Models;

namespace MriAPPDriverShared.ProcessManagement
{
    public static class DriverProcessHelper
    {
        private const string ProcessName = "MriAPPDriver";

        /// <summary>
        /// Returns all currently running MriAPPDriver.exe processes.
        /// </summary>
        public static List<Process> GetRunningDriverProcesses()
        {
            try
            {
                return Process.GetProcessesByName(ProcessName).ToList();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to enumerate {ProcessName} processes.", ex);
            }
        }

        /// <summary>
        /// Returns all MriAPPDriver.exe processes that have been running
        /// longer than the specified threshold.
        /// </summary>
        public static List<Process> GetStaleDriverProcesses(TimeSpan threshold)
        {
            var cutoff = DateTime.Now - threshold;
            return GetRunningDriverProcesses()
                .Where(p =>
                {
                    try   { return p.StartTime < cutoff; }
                    catch { return false; } // Access denied to StartTime on some processes
                })
                .ToList();
        }

        /// <summary>
        /// Returns a single running MriAPPDriver.exe process by PID, or null.
        /// </summary>
        public static Process? GetProcessById(int pid)
        {
            try
            {
                var p = Process.GetProcessById(pid);
                return p.ProcessName.Equals(ProcessName, StringComparison.OrdinalIgnoreCase)
                    ? p
                    : null;
            }
            catch (ArgumentException)
            {
                return null; // Process not found
            }
        }

        /// <summary>
        /// Attempts to kill a process. Returns true on success.
        /// </summary>
        public static bool TryKill(Process process, out string error)
        {
            error = string.Empty;
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Builds a DriverProcessInfo from a Process object (OS-level info only).
        /// Call DriverRepository to enrich with DB info.
        /// </summary>
        public static DriverProcessInfo BuildBasicInfo(Process process)
        {
            DateTime? startTime = null;
            try { startTime = process.StartTime; } catch { }

            return new DriverProcessInfo
            {
                ProcessId = process.Id,
                StartTime = startTime
            };
        }
    }
}
