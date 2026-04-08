using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MriAPPDriverMonitor;

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
