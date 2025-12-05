using Avalonia;
using System;
using System.Threading;
using Edi.Avalonia;
using Edi.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

var thread = new Thread(() =>
{
    using var mutex = new Mutex(true, "Edi", out var createdNew);
    if (!createdNew)
    {
        Environment.Exit(0);
        return;
    }

    var host = Host.CreateDefaultBuilder()
        .UseSerilog((ctx, sp, loggerConfig) =>
        {
            var ediConfig = sp.GetService<Edi.Core.Services.ConfigurationManager>().Get<EdiConfig>();

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

    host.StartAsync().GetAwaiter().GetResult();

    var app = AppBuilder.Configure<App>(() => host.Services.GetRequiredService<App>()).UsePlatformDetect();
    app.StartWithClassicDesktopLifetime([]);

    host.StopAsync().GetAwaiter().GetResult();
    host.Dispose();
});

thread.Start();