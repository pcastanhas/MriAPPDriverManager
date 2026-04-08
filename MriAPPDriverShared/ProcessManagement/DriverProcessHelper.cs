using System;
using System.Collections.Generic;
using System.Management;
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
                Impersonation   = ImpersonationLevel.Impersonate,
                Authentication  = AuthenticationLevel.PacketPrivacy,
                EnablePrivileges = true
            };

            _scope = new ManagementScope(path, options);
        }

        /// <summary>
        /// Connects the WMI scope. Throws if the remote machine is unreachable.
        /// </summary>
        private void EnsureConnected()
        {
            if (!_scope.IsConnected)
                _scope.Connect();
        }

        /// <summary>
        /// Returns all currently running MriAPPDriver.exe processes on the target machine.
        /// </summary>
        public List<DriverProcessInfo> GetRunningDriverProcesses()
        {
            EnsureConnected();
            var results = new List<DriverProcessInfo>();

            var query = new ObjectQuery(
                $"SELECT ProcessId, Name, CreationDate FROM Win32_Process WHERE Name = '{ProcessExeName}'");

            using (var searcher = new ManagementObjectSearcher(_scope, query))
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject obj in collection)
                {
                    results.Add(BuildInfoFromWmiObject(obj));
                    obj.Dispose();
                }
            }

            return results;
        }

        /// <summary>
        /// Returns all MriAPPDriver.exe processes that have been running
        /// longer than the specified threshold on the target machine.
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
        /// Returns info for a single MriAPPDriver.exe process by PID on the target machine, or null.
        /// </summary>
        public DriverProcessInfo? GetProcessById(int pid)
        {
            EnsureConnected();

            var query = new ObjectQuery(
                $"SELECT ProcessId, Name, CreationDate FROM Win32_Process " +
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

            var query = new ObjectQuery(
                $"SELECT * FROM Win32_Process WHERE ProcessId = {pid}");

            try
            {
                using (var searcher = new ManagementObjectSearcher(_scope, query))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject obj in collection)
                    {
                        var result = obj.InvokeMethod("Terminate", null);
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

        /// <summary>
        /// Builds a DriverProcessInfo from a WMI Win32_Process object.
        /// </summary>
        private DriverProcessInfo BuildInfoFromWmiObject(ManagementObject obj)
        {
            DateTime? startTime = null;

            try
            {
                var creationDate = obj["CreationDate"]?.ToString();
                if (!string.IsNullOrEmpty(creationDate))
                    startTime = ManagementDateTimeConverter.ToDateTime(creationDate);
            }
            catch { }

            return new DriverProcessInfo
            {
                ProcessId   = Convert.ToInt32(obj["ProcessId"]),
                MachineName = _machineName,
                StartTime   = startTime
            };
        }
    }
}

