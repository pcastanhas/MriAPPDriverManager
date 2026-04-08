using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using MriAPPDriverShared.Models;

namespace MriAPPDriverShared.ProcessManagement
{
    public class DriverProcessHelper
    {
        private const string ProcessExeName = "MriAPPDriver.exe";
        private readonly string _machineName;
        private readonly ManagementScope _scope;

        public DriverProcessHelper(string machineName)
        {
            _machineName = machineName;

            var isLocal = machineName.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                       || machineName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                       || machineName == ".";

            var path = isLocal
                ? @"\\.\root\cimv2"
                : $@"\\{machineName}\root\cimv2";

            var options = new ConnectionOptions
            {
                Impersonation    = ImpersonationLevel.Impersonate,
                Authentication   = AuthenticationLevel.PacketPrivacy,
                EnablePrivileges = true
            };

            _scope = new ManagementScope(path, options);
        }

        private void EnsureConnected()
        {
            if (!_scope.IsConnected)
                _scope.Connect();
        }

        /// <summary>
        /// Returns all currently running MriAPPDriver.exe processes on the target machine,
        /// including CPU% (two-snapshot) and memory.
        /// </summary>
        public List<DriverProcessInfo> GetRunningDriverProcesses()
        {
            EnsureConnected();

            // ── Snapshot 1 ────────────────────────────────────────────────────
            var snapshot1 = TakeCpuSnapshot();
            var t1        = DateTime.UtcNow;

            // ── Collect base process info + memory ────────────────────────────
            var results = new List<DriverProcessInfo>();
            var query   = new ObjectQuery(
                $"SELECT ProcessId, Name, CreationDate, WorkingSetSize, " +
                $"KernelModeTime, UserModeTime FROM Win32_Process " +
                $"WHERE Name = '{ProcessExeName}'");

            using (var searcher = new ManagementObjectSearcher(_scope, query))
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject obj in collection)
                {
                    results.Add(BuildInfoFromWmiObject(obj));
                    obj.Dispose();
                }
            }

            if (results.Count == 0)
                return results;

            // ── Wait ~1 second then take Snapshot 2 ──────────────────────────
            Thread.Sleep(1000);
            var snapshot2   = TakeCpuSnapshot();
            var elapsedTicks = (DateTime.UtcNow - t1).TotalSeconds * TimeSpan.TicksPerSecond;

            int processorCount = GetProcessorCount();

            // ── Calculate CPU% per process ────────────────────────────────────
            foreach (var info in results)
            {
                if (snapshot1.TryGetValue(info.ProcessId, out long cpu1) &&
                    snapshot2.TryGetValue(info.ProcessId, out long cpu2))
                {
                    var cpuDelta = cpu2 - cpu1;
                    if (cpuDelta >= 0 && elapsedTicks > 0)
                    {
                        info.CpuPercent = Math.Round(
                            (cpuDelta / (elapsedTicks * processorCount)) * 100.0, 1);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Returns all MriAPPDriver.exe processes running longer than the threshold.
        /// </summary>
        public List<DriverProcessInfo> GetStaleDriverProcesses(TimeSpan threshold)
        {
            var cutoff = DateTime.Now - threshold;
            var stale  = new List<DriverProcessInfo>();

            foreach (var info in GetRunningDriverProcesses())
            {
                if (info.StartTime.HasValue && info.StartTime.Value < cutoff)
                    stale.Add(info);
            }

            return stale;
        }

        /// <summary>
        /// Returns info for a single MriAPPDriver.exe process by PID, or null.
        /// </summary>
        public DriverProcessInfo? GetProcessById(int pid)
        {
            EnsureConnected();

            var query = new ObjectQuery(
                $"SELECT ProcessId, Name, CreationDate, WorkingSetSize, " +
                $"KernelModeTime, UserModeTime FROM Win32_Process " +
                $"WHERE Name = '{ProcessExeName}' AND ProcessId = {pid}");

            using (var searcher = new ManagementObjectSearcher(_scope, query))
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject obj in collection)
                {
                    var info = BuildInfoFromWmiObject(obj);
                    obj.Dispose();
                    return info;
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to kill a process on the target machine via WMI. Returns true on success.
        /// </summary>
        public bool TryKill(int pid, out string error)
        {
            error = string.Empty;
            EnsureConnected();

            try
            {
                var query = new ObjectQuery(
                    $"SELECT * FROM Win32_Process WHERE ProcessId = {pid}");

                using (var searcher = new ManagementObjectSearcher(_scope, query))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject obj in collection)
                    {
                        var result     = obj.InvokeMethod("Terminate", null);
                        var returnCode = Convert.ToInt32(result);
                        obj.Dispose();

                        if (returnCode == 0)
                            return true;

                        error = $"WMI Terminate returned code {returnCode}";
                        return false;
                    }
                }

                error = $"Process PID={pid} not found on {_machineName}.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Takes a snapshot of KernelModeTime + UserModeTime for all MriAPPDriver processes.
        /// Returns a dictionary keyed by ProcessId.
        /// </summary>
        private Dictionary<int, long> TakeCpuSnapshot()
        {
            var snapshot = new Dictionary<int, long>();
            var query    = new ObjectQuery(
                $"SELECT ProcessId, KernelModeTime, UserModeTime FROM Win32_Process " +
                $"WHERE Name = '{ProcessExeName}'");

            using (var searcher = new ManagementObjectSearcher(_scope, query))
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject obj in collection)
                {
                    try
                    {
                        int  pid     = Convert.ToInt32(obj["ProcessId"]);
                        long kernel  = Convert.ToInt64(obj["KernelModeTime"]);
                        long user    = Convert.ToInt64(obj["UserModeTime"]);
                        snapshot[pid] = kernel + user;
                    }
                    catch { }
                    finally { obj.Dispose(); }
                }
            }

            return snapshot;
        }

        /// <summary>
        /// Gets the number of logical processors on the target machine via WMI.
        /// Falls back to 1 if unavailable.
        /// </summary>
        private int GetProcessorCount()
        {
            try
            {
                var query = new ObjectQuery(
                    "SELECT NumberOfLogicalProcessors FROM Win32_ComputerSystem");

                using (var searcher = new ManagementObjectSearcher(_scope, query))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject obj in collection)
                    {
                        var count = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                        obj.Dispose();
                        return count > 0 ? count : 1;
                    }
                }
            }
            catch { }

            return 1;
        }

        /// <summary>
        /// Builds a DriverProcessInfo from a WMI Win32_Process object.
        /// </summary>
        private DriverProcessInfo BuildInfoFromWmiObject(ManagementObject obj)
        {
            DateTime? startTime = null;
            double memoryMb     = 0;

            try
            {
                var creationDate = obj["CreationDate"]?.ToString();
                if (!string.IsNullOrEmpty(creationDate))
                    startTime = ManagementDateTimeConverter.ToDateTime(creationDate);
            }
            catch { }

            try
            {
                var workingSet = Convert.ToInt64(obj["WorkingSetSize"]);
                memoryMb = Math.Round(workingSet / 1024.0 / 1024.0, 1);
            }
            catch { }

            return new DriverProcessInfo
            {
                ProcessId   = Convert.ToInt32(obj["ProcessId"]),
                MachineName = _machineName,
                StartTime   = startTime,
                MemoryMb    = memoryMb
            };
        }
    }
}

