using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MriAPPDriverShared.Models;

namespace MriAPPDriverMgrWin
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ProcessRow> _rows = new ObservableCollection<ProcessRow>();
        private readonly DispatcherTimer _refreshTimer;
        private bool _isRefreshing = false;

        public MainWindow()
        {
            InitializeComponent();

            ProcessGrid.ItemsSource = _rows;

            TargetMachineText.Text = $"Target: {App.TargetMachine}";
            FooterText.Text = $"Auto-refresh every {App.RefreshIntervalSeconds}s";

            // Auto-refresh timer
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(App.RefreshIntervalSeconds)
            };
            _refreshTimer.Tick += async (s, e) => await LoadProcessesAsync();
            _refreshTimer.Start();

            // Initial load
            Loaded += async (s, e) => await LoadProcessesAsync();
        }

        // ── Load processes ────────────────────────────────────────────────────

        private async Task LoadProcessesAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            SetLoading(true, "Refreshing...");

            try
            {
                var processes = await Task.Run(async () =>
                {
                    // WMI query + ~1s CPU sampling happens here off the UI thread
                    var infos = App.ProcessHelper.GetRunningDriverProcesses();

                    foreach (var info in infos)
                    {
                        try
                        {
                            var dbInfo = await App.Repository.GetProcessInfoAsync(info.ProcessId);
                            if (dbInfo != null)
                            {
                                info.SessionId    = dbInfo.SessionId;
                                info.UserId       = dbInfo.UserId;
                                info.ReportName   = dbInfo.ReportName;
                                info.ComputerName = dbInfo.ComputerName;
                                if (!info.StartTime.HasValue)
                                    info.StartTime = dbInfo.StartTime;
                            }
                        }
                        catch { /* DB unavailable - show WMI-only data */ }
                    }

                    return infos;
                });

                _rows.Clear();
                foreach (var info in processes)
                    _rows.Add(ToRow(info));

                var now = DateTime.Now.ToString("HH:mm:ss");
                LastRefreshText.Text = $"Last refresh: {now}";
                SetLoading(false, $"Last refreshed: {now} — {processes.Count} process(es) found");
            }
            catch (Exception ex)
            {
                SetLoading(false, $"Error connecting to {App.TargetMachine}: {ex.Message}");
                MessageBox.Show(
                    $"Could not connect to {App.TargetMachine}:\n\n{ex.Message}",
                    "Connection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        // ── Kill button ───────────────────────────────────────────────────────

        private async void KillButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is int pid))
                return;

            var row        = FindRowByPid(pid);
            var reportName = row?.ReportName ?? "unknown report";

            var confirm = MessageBox.Show(
                $"Are you sure you want to kill PID {pid}?\n\nReport: {reportName}",
                "Confirm Kill",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes)
                return;

            SetLoading(true, $"Killing PID {pid}...");

            try
            {
                await Task.Run(() =>
                {
                    if (!App.ProcessHelper.TryKill(pid, out string killError))
                        throw new InvalidOperationException(killError);
                });

                // Build info for the log entry
                var info = new DriverProcessInfo
                {
                    ProcessId   = pid,
                    MachineName = App.TargetMachine,
                    SessionId   = row?.SessionId,
                    UserId      = row?.UserId,
                    ReportName  = row?.ReportName,
                    StartTime   = row != null && DateTime.TryParse(row.StartTime, out var st)
                                    ? st : (DateTime?)null
                };

                App.Logger.LogKilledProcess(info,
                    killedBy: $"WPF Manager (user: {Environment.UserName})");

                if (row != null)
                    _rows.Remove(row);

                SetLoading(false, $"PID {pid} killed successfully.");
            }
            catch (Exception ex)
            {
                App.Logger.LogError(
                    $"Failed to kill PID={pid} via WPF Manager. Error: {ex.Message}");
                SetLoading(false, $"Failed to kill PID {pid}.");
                MessageBox.Show(
                    $"Could not kill PID {pid}:\n\n{ex.Message}",
                    "Kill Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ── Refresh button ────────────────────────────────────────────────────

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadProcessesAsync();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ProcessRow? FindRowByPid(int pid)
        {
            foreach (var row in _rows)
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
            RefreshButton.IsEnabled = !isLoading;
            StatusText.Text         = status;
        }
    }
}
