using Edi.Core;
using Edi.Forms;
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
            var ediConfig = sp.GetService<Edi.Core.ConfigurationManager>().Get<EdiConfig>();

            loggerConfig
                .MinimumLevel.Debug()
                .WriteTo.Conditional(
                    _ => ediConfig.UseLogs,
                    wt => wt.File("./Edilog.txt",
                                rollingInterval: RollingInterval.Day,
                                retainedFileCountLimit: 1)
                );
        })
        .ConfigureServices((context, services) =>
        {
            services.AddEdi("./EdiConfig.json");
            services.AddTransient<App>();
            services.AddHostedService<EdiHostedService>(); // 👈 tu servicio background
        })
        .Build();

    // 🔥 Arranca los Hosted Services manualmente
    host.StartAsync().GetAwaiter().GetResult();

    var app = host.Services.GetRequiredService<App>();
    app.Run();

    // 🧹 Paramos servicios al cerrar la app
    host.StopAsync().GetAwaiter().GetResult();
    host.Dispose();
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
