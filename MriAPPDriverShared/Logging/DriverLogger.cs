using System;
using System.Diagnostics;
using System.IO;
using MriAPPDriverShared.Models;

namespace MriAPPDriverShared.Logging
{
    public class DriverLogger
    {
        private readonly string _logDirectory;
        private readonly string _eventSource;
        private readonly string _eventLog;

        public DriverLogger(string baseDirectory, string eventSource = "MriAPPDriverManager", string eventLog = "Application")
        {
            _logDirectory = Path.Combine(baseDirectory, "log");
            _eventSource  = eventSource;
            _eventLog     = eventLog;

            Directory.CreateDirectory(_logDirectory);

            // Register event source if not already registered (requires admin on first run)
            try
            {
                if (!EventLog.SourceExists(_eventSource))
                    EventLog.CreateEventSource(_eventSource, _eventLog);
            }
            catch (Exception ex)
            {
                // If we can't register, we still continue - log file will work
                WriteToFile($"[WARN] Could not register Windows Event Log source '{_eventSource}': {ex.Message}");
            }
        }

        private string TodayLogPath =>
            Path.Combine(_logDirectory, $"mriappdriver-{DateTime.Now:yyyy-MM-dd}.log");

        /// <summary>
        /// Logs a killed/crashed process to both the daily log file and Windows Event Log.
        /// </summary>
        public void LogKilledProcess(DriverProcessInfo info, string killedBy)
        {
            var message = BuildProcessMessage(info, killedBy);
            WriteToFile(message);
            WriteToEventLog(message, EventLogEntryType.Warning);
        }

        /// <summary>
        /// Logs a general informational message to the daily log file only.
        /// </summary>
        public void LogInfo(string message)
        {
            WriteToFile($"[INFO]  {message}");
        }

        /// <summary>
        /// Logs an error to both the daily log file and Windows Event Log.
        /// </summary>
        public void LogError(string message, Exception? ex = null)
        {
            var full = ex != null ? $"{message} | Exception: {ex}" : message;
            WriteToFile($"[ERROR] {full}");
            WriteToEventLog(full, EventLogEntryType.Error);
        }

        private void WriteToFile(string line)
        {
            try
            {
                var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}";
                File.AppendAllText(TodayLogPath, entry);
            }
            catch
            {
                // Best effort - don't crash the service over a logging failure
            }
        }

        private void WriteToEventLog(string message, EventLogEntryType type)
        {
            try
            {
                EventLog.WriteEntry(_eventSource, message, type, 1001);
            }
            catch
            {
                // Best effort
            }
        }

        private static string BuildProcessMessage(DriverProcessInfo info, string killedBy)
        {
            var duration = info.RunDuration.HasValue
                ? $"{info.RunDuration.Value.Hours:D2}h {info.RunDuration.Value.Minutes:D2}m {info.RunDuration.Value.Seconds:D2}s"
                : "unknown";

            return $"[KILLED] PID={info.ProcessId,-6} | " +
                   $"MsgKey={info.MessageKey?.ToString() ?? "N/A",-10} | " +
                   $"User={info.UserId ?? "N/A",-15} | " +
                   $"Started={info.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"} | " +
                   $"Duration={duration} | " +
                   $"Report={info.ReportName ?? "N/A"} | " +
                   $"KilledBy={killedBy}";
        }
    }
}
