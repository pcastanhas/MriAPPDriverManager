using System;
using System.Collections.Generic;
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
        private readonly DriverProcessHelper _processHelper;

        public MonitorWorker(
            IOptions<MonitorSettings> settings,
            string connectionString)
        {
            _settings      = settings.Value;
            _repository    = new DriverRepository(connectionString);
            _logger        = new DriverLogger(AppContext.BaseDirectory, eventSource: "MriAPPDriverMonitor");
            _processHelper = new DriverProcessHelper(_settings.TargetMachine);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInfo(
                $"MriAPPDriverMonitor started. " +
                $"Target: {_settings.TargetMachine} | " +
                $"Polling every {_settings.PollingIntervalSeconds}s | " +
                $"Threshold: {_settings.ProcessAgeThresholdMinutes} minutes.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ScanAndKillStaleProcessesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Unhandled error during scan cycle on {_settings.TargetMachine}.", ex);
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

            List<MriAPPDriverShared.Models.DriverProcessInfo> staleProcesses;
            try
            {
                staleProcesses = _processHelper.GetStaleDriverProcesses(threshold);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Could not connect to {_settings.TargetMachine} to enumerate processes.", ex);
                return;
            }

            if (staleProcesses.Count == 0)
                return;

            _logger.LogInfo($"Found {staleProcesses.Count} stale MriAPPDriver.exe process(es) on {_settings.TargetMachine}.");

            foreach (var info in staleProcesses)
            {
                try
                {
                    // Enrich with database info
                    var dbInfo = await _repository.GetProcessInfoAsync(info.ProcessId);
                    if (dbInfo != null)
                    {
                        info.SessionId    = dbInfo.SessionId;
                        info.UserId       = dbInfo.UserId;
                        info.ReportName   = dbInfo.ReportName;
                        info.Description  = dbInfo.Description;
                        info.ComputerName = dbInfo.ComputerName;
                        if (!info.StartTime.HasValue)
                            info.StartTime = dbInfo.StartTime;
                    }

                    // Kill the process via WMI
                    if (_processHelper.TryKill(info.ProcessId, out string killError))
                    {
                        _logger.LogKilledProcess(info, killedBy: "Monitor Service");
                    }
                    else
                    {
                        _logger.LogError(
                            $"Failed to kill PID={info.ProcessId} on {_settings.TargetMachine} | " +
                            $"Report={info.ReportName ?? "N/A"} | Error: {killError}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error handling PID={info.ProcessId} on {_settings.TargetMachine}.", ex);
                }
            }
        }
    }
}
