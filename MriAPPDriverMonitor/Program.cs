using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MriAPPDriverMonitor
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            IHost host = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options =>
                {
                    options.ServiceName = "MriAPPDriverMonitor";
                })
                .ConfigureServices((context, services) =>
                {
                    services.Configure<MonitorSettings>(
                        context.Configuration.GetSection("MonitorSettings"));

                    services.AddSingleton<string>(_ =>
                        context.Configuration.GetConnectionString("MriDatabase")
                        ?? throw new InvalidOperationException(
                            "Connection string 'MriDatabase' not found in appsettings.json"));

                    services.AddHostedService<MonitorWorker>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}
