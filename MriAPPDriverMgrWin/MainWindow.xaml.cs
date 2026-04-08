using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MriAPPDriverShared.Models;

namespace MriAPPDriverMgrWin
{
    public sealed partial class MainWindow : Window
    {
        public ObservableCollection<ProcessRow> ProcessRows { get; } = new ObservableCollection<ProcessRow>();

        private readonly DispatcherQueue _dispatcher;
        private DispatcherQueueTimer? _refreshTimer;
        private bool _isRefreshing = false;

        public MainWindow()
        {
            InitializeComponent();

            _dispatcher = DispatcherQueue.GetForCurrentThread();

            TargetMachineText.Text = $"Target: {App.TargetMachine}";
            FooterText.Text = $"Auto-refresh every {App.RefreshIntervalSeconds}s";

            StartRefreshTimer();

            // Initial load
            _ = LoadProcessesAsync();
        }

        // ── Timer ────────────────────────────────────────────────────────────

        private void StartRefreshTimer()
        {
            _refreshTimer = _dispatcher.CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(App.RefreshIntervalSeconds);
            _refreshTimer.Tick += (s, e) => _ = LoadProcessesAsync();
            _refreshTimer.IsRepeating = true;
            _refreshTimer.Start();
        }

        // ── Load processes ────────────────────────────────────────────────────

        private async Task LoadProcessesAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            SetLoading(true, "Refreshing...");

            try
            {
                // Run WMI query off the UI thread (includes ~1s CPU sampling)
                var processes = await Task.Run(async () =>
                {
                    var infos = App.ProcessHelper.GetRunningDriverProcesses();

                    // Enrich each with DB info
                    foreach (var info in infos)
                    {
                        try
                        {
                            var dbInfo = await App.Repository.GetProcessInfoAsync(info.ProcessId);
                            if (dbInfo != null)
                            {
                                info.SessionId   = dbInfo.SessionId;
                                info.UserId      = dbInfo.UserId;
                                info.ReportName  = dbInfo.ReportName;
                                info.ComputerName = dbInfo.ComputerName;
                                if (!info.StartTime.HasValue)
                                    info.StartTime = dbInfo.StartTime;
                            }
                        }
                        catch { /* DB unavailable — show WMI-only data */ }
                    }

                    return infos;
                });

                // Update grid on UI thread
                ProcessRows.Clear();
                foreach (var info in processes)
                    ProcessRows.Add(ToRow(info));

                var now = DateTime.Now.ToString("HH:mm:ss");
                SetLoading(false, $"Last refreshed: {now} — {processes.Count} process(es)");
                LastRefreshText.Text = $"Last refresh: {now}";
            }
            catch (Exception ex)
            {
                SetLoading(false, $"Error: {ex.Message}");
                await ShowErrorAsync("Connection Error",
                    $"Could not connect to {App.TargetMachine}:\n\n{ex.Message}");
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        // ── Kill button ───────────────────────────────────────────────────────

        private async void KillButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not int pid)
                return;

            var row = FindRowByPid(pid);
            var reportName = row?.ReportName ?? "unknown report";

            // Confirmation dialog
            var dialog = new ContentDialog
            {
                Title           = "Confirm Kill",
                Content         = $"Are you sure you want to kill PID {pid}?\n\nReport: {reportName}",
                PrimaryButtonText   = "Kill",
                CloseButtonText     = "Cancel",
                DefaultButton   = ContentDialogButton.Close,
                XamlRoot        = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            SetLoading(true, $"Killing PID {pid}...");

            try
            {
                var killed = await Task.Run(() =>
                {
                    if (!App.ProcessHelper.TryKill(pid, out string killError))
                        throw new InvalidOperationException(killError);
                    return true;
                });

                // Build info for logging
                var info = new DriverProcessInfo
                {
                    ProcessId  = pid,
                    MachineName = App.TargetMachine,
                    SessionId  = row?.SessionId,
                    UserId     = row?.UserId,
                    ReportName = row?.ReportName,
                    StartTime  = row != null && DateTime.TryParse(row.StartTime, out var st) ? st : (DateTime?)null
                };

                App.Logger.LogKilledProcess(info,
                    killedBy: $"WinUI Manager (user: {Environment.UserName})");

                // Remove from grid
                if (row != null)
                    ProcessRows.Remove(row);

                SetLoading(false, $"PID {pid} killed successfully.");
            }
            catch (Exception ex)
            {
                App.Logger.LogError($"Failed to kill PID={pid} via WinUI Manager. Error: {ex.Message}");
                SetLoading(false, $"Failed to kill PID {pid}.");
                await ShowErrorAsync("Kill Failed",
                    $"Could not kill PID {pid}:\n\n{ex.Message}");
            }
        }

        // ── Refresh button ────────────────────────────────────────────────────

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadProcessesAsync();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ProcessRow? FindRowByPid(int pid)
        {
            foreach (var row in ProcessRows)
                if (row.ProcessId == pid) return row;
            return null;
        }

        private static ProcessRow ToRow(DriverProcessInfo info)
        {
            return new ProcessRow
            {
                MachineName = info.MachineName ?? string.Empty,
                ProcessId   = info.ProcessId,
                SessionId   = info.SessionId  ?? "N/A",
                UserId      = info.UserId     ?? "N/A",
                StartTime   = info.StartTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A",
                Running     = FormatDuration(info.RunDuration),
                CpuPercent  = info.CpuPercent,
                MemoryMb    = info.MemoryMb,
                ReportName  = info.ReportName ?? "N/A"
            };
        }

        private static string FormatDuration(TimeSpan? duration)
        {
            if (!duration.HasValue) return "N/A";
            return duration.Value.TotalHours >= 1
                ? $"{(int)duration.Value.TotalHours}h {duration.Value.Minutes:D2}m"
                : $"{duration.Value.Minutes}m {duration.Value.Seconds:D2}s";
        }

        private void SetLoading(bool isLoading, string status)
        {
            LoadingRing.IsActive = isLoading;
            RefreshButton.IsEnabled = !isLoading;
            StatusText.Text = status;
        }

        private async Task ShowErrorAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title           = title,
                Content         = message,
                CloseButtonText = "OK",
                XamlRoot        = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
