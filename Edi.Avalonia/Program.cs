using Avalonia;
using System;
using System.Threading;
using Edi.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Edi.Avalonia;

internal static class Program
{
    private static IServiceProvider serviceProvider = null!;

    private static int Main(string[] args)
    {
        using var mutex = new Mutex(true, "Edi", out var createdNew);
        if (!createdNew)
        {
            Environment.Exit(0);
        }

        var host = CreateHost();
        host.StartAsync().GetAwaiter().GetResult();

        var builder = AppBuilder.Configure(() => serviceProvider.GetRequiredService<App>()).UsePlatformDetect();
        var exitCode = builder.StartWithClassicDesktopLifetime([]);

        host.StopAsync().GetAwaiter().GetResult();
        host.Dispose();

        return exitCode;
    }

    /// <summary>
    /// This method is needed for IDE previewer infrastructure
    /// </summary>
    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure(() =>
    {
        CreateHost();
        return serviceProvider.GetRequiredService<App>();
    }).UsePlatformDetect();

    private static IHost CreateHost()
    {
        var host = Host.CreateDefaultBuilder()
            .UseSerilog((ctx, sp, loggerConfig) =>
            {
                var ediConfig = sp.GetService<Core.Services.ConfigurationManager>()!.Get<EdiConfig>();

                loggerConfig
                    .MinimumLevel.Debug()
                    .WriteTo.Conditional(
                        _ => ediConfig.UseLogs,
                        wt => wt.File(
                            "./Edilog.txt",
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 1)
                    );
            })
            .ConfigureServices((context, services) =>
            {
                services.AddEdi("./EdiConfig.json");
                services.AddTransient<App>();
            })
            .Build();

        serviceProvider = host.Services;

        return host;
    }
}
