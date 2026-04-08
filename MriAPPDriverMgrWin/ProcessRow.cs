using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MriAPPDriverMgrWin
{
    public class ProcessRow : INotifyPropertyChanged
    {
        private string _machineName = string.Empty;
        private int    _processId;
        private int?   _messageKey;
        private string _userId     = string.Empty;
        private string _startTime  = string.Empty;
        private string _running    = string.Empty;
        private double _cpuPercent;
        private double _memoryMb;
        private string _reportName = string.Empty;

        public string MachineName
        {
            get => _machineName;
            set { _machineName = value; OnPropertyChanged(); }
        }

        public int ProcessId
        {
            get => _processId;
            set { _processId = value; OnPropertyChanged(); }
        }

        public int? MessageKey
        {
            get => _messageKey;
            set { _messageKey = value; OnPropertyChanged(); }
        }

        public string UserId
        {
            get => _userId;
            set { _userId = value; OnPropertyChanged(); }
        }

        public string StartTime
        {
            get => _startTime;
            set { _startTime = value; OnPropertyChanged(); }
        }

        public string Running
        {
            get => _running;
            set { _running = value; OnPropertyChanged(); }
        }

        public double CpuPercent
        {
            get => _cpuPercent;
            set { _cpuPercent = value; OnPropertyChanged(); }
        }

        public double MemoryMb
        {
            get => _memoryMb;
            set { _memoryMb = value; OnPropertyChanged(); }
        }

        public string ReportName
        {
            get => _reportName;
            set { _reportName = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
