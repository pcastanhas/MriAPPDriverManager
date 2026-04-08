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

            _repository = new DriverRepository(connectionString);
            _logger     = new DriverLogger(AppContext.BaseDirectory, eventSource: "MriAPPDriverManager");

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
            var processes = DriverProcessHelper.GetRunningDriverProcesses();

            if (processes.Count == 0)
            {
                Console.WriteLine("No MriAPPDriver.exe processes are currently running.");
                return 0;
            }

            Console.WriteLine($"Found {processes.Count} running MriAPPDriver.exe process(es):");
            Console.WriteLine(new string('-', 100));
            PrintHeader();
            Console.WriteLine(new string('-', 100));

            foreach (var process in processes)
            {
                var info   = DriverProcessHelper.BuildBasicInfo(process);
                var dbInfo = await _repository.GetProcessInfoAsync(process.Id);
                MergeDbInfo(info, dbInfo);
                PrintProcessRow(info);
                process.Dispose();
            }

            Console.WriteLine(new string('-', 100));
            return 0;
        }

        private static async Task<int> HandleInfoAsync(int targetPid)
        {
            var process = DriverProcessHelper.GetProcessById(targetPid);
            if (process == null)
            {
                Console.Error.WriteLine($"[ERROR] No running MriAPPDriver.exe process found with PID {targetPid}.");
                return 1;
            }

            var info   = DriverProcessHelper.BuildBasicInfo(process);
            var dbInfo = await _repository.GetProcessInfoAsync(targetPid);
            MergeDbInfo(info, dbInfo);

            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"  PID        : {info.ProcessId}");
            Console.WriteLine($"  Session ID : {info.SessionId ?? "N/A (not found in DB)"}");
            Console.WriteLine($"  User ID    : {info.UserId ?? "N/A"}");
            Console.WriteLine($"  Start Time : {info.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"}");
            Console.WriteLine($"  Running For: {FormatDuration(info.RunDuration)}");
            Console.WriteLine($"  Report     : {info.ReportName ?? "N/A"}");
            Console.WriteLine($"  Computer   : {info.ComputerName ?? "N/A"}");
            Console.WriteLine(new string('-', 60));

            process.Dispose();
            return 0;
        }

        private static async Task<int> HandleKillAsync(int targetPid)
        {
            var process = DriverProcessHelper.GetProcessById(targetPid);
            if (process == null)
            {
                Console.Error.WriteLine($"[ERROR] No running MriAPPDriver.exe process found with PID {targetPid}.");
                return 1;
            }

            var info   = DriverProcessHelper.BuildBasicInfo(process);
            var dbInfo = await _repository.GetProcessInfoAsync(targetPid);
            MergeDbInfo(info, dbInfo);

            Console.WriteLine($"Killing PID {targetPid} ({info.ReportName ?? "unknown report"})...");

            if (DriverProcessHelper.TryKill(process, out string killError))
            {
                _logger.LogKilledProcess(info, killedBy: $"Manager CLI (user: {Environment.UserName})");
                Console.WriteLine($"[OK] PID {targetPid} killed successfully.");
                Console.WriteLine($"     Session: {info.SessionId ?? "N/A"} | User: {info.UserId ?? "N/A"} | Report: {info.ReportName ?? "N/A"}");
            }
            else
            {
                _logger.LogError($"Failed to kill PID={targetPid} via Manager CLI. Error: {killError}");
                Console.Error.WriteLine($"[ERROR] Could not kill PID {targetPid}: {killError}");
                process.Dispose();
                return 1;
            }

            process.Dispose();
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
            Console.WriteLine($"{"PID",-8} {"Session ID",-14} {"User ID",-16} {"Start Time",-22} {"Running",-12} {"Report",-40}");
        }

        private static void PrintProcessRow(DriverProcessInfo info)
        {
            Console.WriteLine(
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
            Console.WriteLine("  --running          List all running MriAPPDriver.exe processes with report info");
            Console.WriteLine("  --info   <pid>     Show detailed info for a specific MriAPPDriver.exe process");
            Console.WriteLine("  --kill   <pid>     Kill a specific MriAPPDriver.exe process and log the event");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  MriAPPDriverManager --running");
            Console.WriteLine("  MriAPPDriverManager --info 12308");
            Console.WriteLine("  MriAPPDriverManager --kill 12308");
            Console.WriteLine();
        }
    }
}
