using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MriAPPDriverShared.Data;
using MriAPPDriverShared.Logging;
using MriAPPDriverShared.ProcessManagement;

namespace MriAPPDriverMonitor
{
    public class MonitorWorker : BackgroundService
    {
        private readonly MonitorSettings _settings;
        private readonly DriverRepository _repository;
        private readonly DriverLogger _logger;

        public MonitorWorker(
            IOptions<MonitorSettings> settings,
            string connectionString)
        {
            _settings   = settings.Value;
            _repository = new DriverRepository(connectionString);
            _logger     = new DriverLogger(
                AppContext.BaseDirectory,
                eventSource: "MriAPPDriverMonitor");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInfo($"MriAPPDriverMonitor started. " +
                $"Polling every {_settings.PollingIntervalSeconds}s. " +
                $"Threshold: {_settings.ProcessAgeThresholdMinutes} minutes.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ScanAndKillStaleProcessesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError("Unhandled error during scan cycle.", ex);
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(_settings.PollingIntervalSeconds),
                    stoppingToken);
            }

            _logger.LogInfo("MriAPPDriverMonitor stopped.");
        }

        private async Task ScanAndKillStaleProcessesAsync()
        {
            var threshold = TimeSpan.FromMinutes(_settings.ProcessAgeThresholdMinutes);
            var staleProcesses = DriverProcessHelper.GetStaleDriverProcesses(threshold);

            if (staleProcesses.Count == 0)
                return;

            _logger.LogInfo($"Found {staleProcesses.Count} stale MriAPPDriver.exe process(es).");

            foreach (var process in staleProcesses)
            {
                try
                {
                    // Build basic info from the OS process object
                    var info = DriverProcessHelper.BuildBasicInfo(process);

                    // Enrich with database info
                    var dbInfo = await _repository.GetProcessInfoAsync(process.Id);
                    if (dbInfo != null)
                    {
                        info.SessionId    = dbInfo.SessionId;
                        info.UserId       = dbInfo.UserId;
                        info.ReportName   = dbInfo.ReportName;
                        info.Description  = dbInfo.Description;
                        info.ComputerName = dbInfo.ComputerName;
                        // Prefer DB start time if OS start time is unavailable
                        if (!info.StartTime.HasValue)
                            info.StartTime = dbInfo.StartTime;
                    }

                    // Kill the process
                    if (DriverProcessHelper.TryKill(process, out string killError))
                    {
                        _logger.LogKilledProcess(info, killedBy: "Monitor Service");
                    }
                    else
                    {
                        _logger.LogError(
                            $"Failed to kill PID={process.Id} | Report={info.ReportName ?? "N/A"} | Error: {killError}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error handling PID={process.Id}.", ex);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}
