using Edi.Core;
using Edi.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

Thread thread = new Thread(() =>
{
    bool createdNew;
    using (Mutex mutex = new Mutex(true, "Edi", out createdNew))
    {
        if (!createdNew)
        {
            Environment.Exit(0);
            return;
        }
    }

    var host = Host.CreateDefaultBuilder()
        .UseSerilog((ctx, sp, loggerConfig) =>
        {
            loggerConfig
                .MinimumLevel.Debug()
                .WriteTo.File(
                    "./Edilog.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 3,
                    flushToDiskInterval: TimeSpan.FromSeconds(1));
        })
        .ConfigureServices((context, services) =>
        {
            services.AddEdi("./EdiConfig.json");
            services.AddTransient<App>();
        })
        .Build();

    host.StartAsync().GetAwaiter().GetResult();

    var app = host.Services.GetRequiredService<App>();

    app.Run();

    // 🧹 Paramos servicios al cerrar la app
    host.StopAsync().GetAwaiter().GetResult();
    host.Dispose();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
