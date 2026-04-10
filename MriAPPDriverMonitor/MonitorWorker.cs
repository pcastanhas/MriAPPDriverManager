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
                    await ScanAndLogProcessesAsync();
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

        /// <summary>
        /// Main poll cycle: enumerates all running MriAPPDriver.exe processes, enriches each
        /// with DB data, logs the full snapshot, then kills any that exceed the stale threshold.
        /// </summary>
        private async Task ScanAndLogProcessesAsync()
        {
            // ── 1. Enumerate all running processes (includes CPU% + memory) ────
            List<MriAPPDriverShared.Models.DriverProcessInfo> allProcesses;
            try
            {
                allProcesses = _processHelper.GetRunningDriverProcesses();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Could not connect to {_settings.TargetMachine} to enumerate processes.", ex);
                return;
            }

            // ── 2. Enrich every process with DB data ──────────────────────────
            foreach (var info in allProcesses)
            {
                try
                {
                    var dbInfo = await _repository.GetProcessInfoAsync(info.ProcessId);
                    if (dbInfo != null)
                    {
                        info.MessageKey   = dbInfo.MessageKey;
                        info.UserId       = dbInfo.UserId;
                        info.ReportName   = dbInfo.ReportName;
                        info.Description  = dbInfo.Description;
                        info.ComputerName = dbInfo.ComputerName;
                        if (!info.StartTime.HasValue)
                            info.StartTime = dbInfo.StartTime;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Could not retrieve DB info for PID={info.ProcessId}.", ex);
                }
            }

            // ── 3. Log the full snapshot (same data points as the WPF manager) ─
            _logger.LogRunningProcesses(allProcesses, _settings.TargetMachine);

            // ── 4. Kill any processes that exceed the stale threshold ──────────
            var threshold = TimeSpan.FromMinutes(_settings.ProcessAgeThresholdMinutes);
            var staleProcesses = allProcesses.FindAll(
                p => p.StartTime.HasValue && p.StartTime.Value < DateTime.Now - threshold);

            if (staleProcesses.Count == 0)
                return;

            _logger.LogInfo($"Found {staleProcesses.Count} stale MriAPPDriver.exe process(es) on {_settings.TargetMachine}.");

            foreach (var info in staleProcesses)
            {
                try
                {
                    if (_processHelper.TryKill(info.ProcessId, out string killError))
                    {
                        _logger.LogKilledProcess(info, killedBy: "Monitor Service");

                        // Update MRI_Server_Messages status
                        if (info.MessageKey.HasValue)
                        {
                            try
                            {
                                await _repository.UpdateProcessStatusKilledAsync(info.MessageKey.Value);
                            }
                            catch (Exception dbEx)
                            {
                                _logger.LogError($"Failed to update Status for MessageKey={info.MessageKey} after kill.", dbEx);
                            }
                        }
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
