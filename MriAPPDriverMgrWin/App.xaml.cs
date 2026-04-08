using System;
using Microsoft.Extensions.Configuration;
using Microsoft.UI.Xaml;
using MriAPPDriverShared.Data;
using MriAPPDriverShared.Logging;
using MriAPPDriverShared.ProcessManagement;

namespace MriAPPDriverMgrWin
{
    public partial class App : Application
    {
        public static DriverProcessHelper ProcessHelper { get; private set; } = null!;
        public static DriverRepository Repository { get; private set; } = null!;
        public static DriverLogger Logger { get; private set; } = null!;
        public static string TargetMachine { get; private set; } = "localhost";
        public static int RefreshIntervalSeconds { get; private set; } = 30;

        private Window? _mainWindow;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Load configuration
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            var connectionString = config.GetConnectionString("MriDatabase")
                ?? throw new InvalidOperationException(
                    "Connection string 'MriDatabase' not found in appsettings.json");

            TargetMachine          = config["AppSettings:TargetMachine"] ?? "localhost";
            RefreshIntervalSeconds = int.TryParse(config["AppSettings:RefreshIntervalSeconds"], out int interval)
                ? interval : 30;

            ProcessHelper = new DriverProcessHelper(TargetMachine);
            Repository    = new DriverRepository(connectionString);
            Logger        = new DriverLogger(AppContext.BaseDirectory, eventSource: "MriAPPDriverMgrWin");

            _mainWindow = new MainWindow();
            _mainWindow.Activate();
        }
    }
}
