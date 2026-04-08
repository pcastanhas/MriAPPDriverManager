using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MriAPPDriverShared.Data;
using MriAPPDriverShared.Logging;
using MriAPPDriverShared.Models;
using MriAPPDriverShared.ProcessManagement;

namespace MriAPPDriverManager
{
    internal class Program
    {
        private static DriverRepository _repository = null!;
        private static DriverLogger _logger = null!;
        private static DriverProcessHelper _processHelper = null!;
        private static string _targetMachine = "localhost";

        private static async Task<int> Main(string[] args)
        {
            // ─── Bootstrap ────────────────────────────────────────────────────
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            var connectionString = config.GetConnectionString("MriDatabase")
                ?? throw new InvalidOperationException(
                    "Connection string 'MriDatabase' not found in appsettings.json");

            _targetMachine = config["AppSettings:TargetMachine"] ?? "localhost";
            _repository    = new DriverRepository(connectionString);
            _logger        = new DriverLogger(AppContext.BaseDirectory, eventSource: "MriAPPDriverManager");
            _processHelper = new DriverProcessHelper(_targetMachine);

            // ─── Argument Parsing ─────────────────────────────────────────────
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            string action = args[0].ToLowerInvariant();

            bool requiresPid = action == "--info" || action == "--kill";

            int pid = 0;
            if (requiresPid)
            {
                if (args.Length < 2 || !int.TryParse(args[1], out pid) || pid <= 0)
                {
                    Console.Error.WriteLine($"[ERROR] '{action}' requires a valid PID as the second argument.");
                    Console.Error.WriteLine($"        Example: MriAPPDriverManager {action} 12308");
                    return 1;
                }
            }

            // ─── Dispatch ─────────────────────────────────────────────────────
            switch (action)
            {
                case "--running": return await HandleRunningAsync();
                case "--info":    return await HandleInfoAsync(pid);
                case "--kill":    return await HandleKillAsync(pid);
                default:          return HandleUnknownAction(action);
            }
        }

        // ─── Handlers ─────────────────────────────────────────────────────────

        private static async Task<int> HandleRunningAsync()
        {
            List<DriverProcessInfo> processes;
            try
            {
                processes = _processHelper.GetRunningDriverProcesses();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Could not connect to {_targetMachine}: {ex.Message}");
                return 1;
            }

            if (processes.Count == 0)
            {
                Console.WriteLine($"No MriAPPDriver.exe processes are currently running on {_targetMachine}.");
                return 0;
            }

            Console.WriteLine($"Found {processes.Count} running MriAPPDriver.exe process(es) on {_targetMachine}:");
            Console.WriteLine(new string('-', 110));
            PrintHeader();
            Console.WriteLine(new string('-', 110));

            foreach (var info in processes)
            {
                var dbInfo = await _repository.GetProcessInfoAsync(info.ProcessId);
                MergeDbInfo(info, dbInfo);
                PrintProcessRow(info);
            }

            Console.WriteLine(new string('-', 110));
            return 0;
        }

        private static async Task<int> HandleInfoAsync(int targetPid)
        {
            DriverProcessInfo? info;
            try
            {
                info = _processHelper.GetProcessById(targetPid);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Could not connect to {_targetMachine}: {ex.Message}");
                return 1;
            }

            if (info == null)
            {
                Console.Error.WriteLine($"[ERROR] No running MriAPPDriver.exe process found with PID {targetPid} on {_targetMachine}.");
                return 1;
            }

            var dbInfo = await _repository.GetProcessInfoAsync(targetPid);
            MergeDbInfo(info, dbInfo);

            Console.WriteLine(new string('-', 110));
            Console.WriteLine(
                $"{"Machine",-18} {"PID",-8} {"Session ID",-14} {"User ID",-16} " +
                $"{"Start Time",-22} {"Running",-12} {"Report",-40}");
            Console.WriteLine(new string('-', 110));
            Console.WriteLine(
                $"{(info.MachineName ?? _targetMachine),-18} " +
                $"{info.ProcessId,-8} " +
                $"{(info.SessionId ?? "N/A"),-14} " +
                $"{(info.UserId ?? "N/A"),-16} " +
                $"{info.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A",-22} " +
                $"{FormatDuration(info.RunDuration),-12} " +
                $"{Truncate(info.ReportName ?? "N/A", 40),-40}");
            Console.WriteLine(new string('-', 110));

            return 0;
        }

        private static async Task<int> HandleKillAsync(int targetPid)
        {
            DriverProcessInfo? info;
            try
            {
                info = _processHelper.GetProcessById(targetPid);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Could not connect to {_targetMachine}: {ex.Message}");
                return 1;
            }

            if (info == null)
            {
                Console.Error.WriteLine($"[ERROR] No running MriAPPDriver.exe process found with PID {targetPid} on {_targetMachine}.");
                return 1;
            }

            var dbInfo = await _repository.GetProcessInfoAsync(targetPid);
            MergeDbInfo(info, dbInfo);

            Console.WriteLine($"Killing PID {targetPid} on {_targetMachine} ({info.ReportName ?? "unknown report"})...");

            if (_processHelper.TryKill(targetPid, out string killError))
            {
                _logger.LogKilledProcess(info, killedBy: $"Manager CLI (user: {Environment.UserName})");
                Console.WriteLine($"[OK] PID {targetPid} on {_targetMachine} killed successfully.");
                Console.WriteLine($"     Session: {info.SessionId ?? "N/A"} | User: {info.UserId ?? "N/A"} | Report: {info.ReportName ?? "N/A"}");
            }
            else
            {
                _logger.LogError($"Failed to kill PID={targetPid} on {_targetMachine} via Manager CLI. Error: {killError}");
                Console.Error.WriteLine($"[ERROR] Could not kill PID {targetPid} on {_targetMachine}: {killError}");
                return 1;
            }

            return 0;
        }

        private static int HandleUnknownAction(string unknownAction)
        {
            Console.Error.WriteLine($"[ERROR] Unknown action: '{unknownAction}'");
            PrintUsage();
            return 1;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static void MergeDbInfo(DriverProcessInfo target, DriverProcessInfo? source)
        {
            if (source == null) return;
            target.SessionId    = source.SessionId;
            target.UserId       = source.UserId;
            target.ReportName   = source.ReportName;
            target.Description  = source.Description;
            target.ComputerName = source.ComputerName;
            if (!target.StartTime.HasValue)
                target.StartTime = source.StartTime;
        }

        private static void PrintHeader()
        {
            Console.WriteLine(
                $"{"Machine",-18} {"PID",-8} {"Session ID",-14} {"User ID",-16} " +
                $"{"Start Time",-22} {"Running",-12} {"Report",-40}");
        }

        private static void PrintProcessRow(DriverProcessInfo info)
        {
            Console.WriteLine(
                $"{(info.MachineName ?? _targetMachine),-18} " +
                $"{info.ProcessId,-8} " +
                $"{(info.SessionId ?? "N/A"),-14} " +
                $"{(info.UserId ?? "N/A"),-16} " +
                $"{info.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A",-22} " +
                $"{FormatDuration(info.RunDuration),-12} " +
                $"{Truncate(info.ReportName ?? "N/A", 40),-40}");
        }

        private static string FormatDuration(TimeSpan? duration)
        {
            if (!duration.HasValue) return "N/A";
            return duration.Value.TotalHours >= 1
                ? $"{(int)duration.Value.TotalHours}h {duration.Value.Minutes:D2}m"
                : $"{duration.Value.Minutes}m {duration.Value.Seconds:D2}s";
        }

        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";

        private static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: MriAPPDriverManager <action> [pid]");
            Console.WriteLine();
            Console.WriteLine("Actions:");
            Console.WriteLine("  --running          List all running MriAPPDriver.exe processes on the target machine");
            Console.WriteLine("  --info   <pid>     Show detailed info for a specific MriAPPDriver.exe process");
            Console.WriteLine("  --kill   <pid>     Kill a specific MriAPPDriver.exe process and log the event");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  MriAPPDriverManager --running");
            Console.WriteLine("  MriAPPDriverManager --info 12308");
            Console.WriteLine("  MriAPPDriverManager --kill 12308");
            Console.WriteLine();
            Console.WriteLine("Target machine is configured in appsettings.json under AppSettings:TargetMachine.");
            Console.WriteLine();
        }
    }
}
