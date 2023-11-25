using Edi.Core;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Windows;
using Microsoft.OpenApi.Models;

namespace Edi.Forms
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly ServiceProvider serviceProvider;
        private readonly WebApplication webApp;
        public static IEdi Edi;
        public App()
        {
            
            var webAppBuilder = WebApplication.CreateBuilder();
            
            webAppBuilder.WebHost.UseUrls("http://localhost:5000");

            IServiceCollection services = webAppBuilder.Services;

            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Edi Rest", Version = "v1" });
            });

            services.AddSingleton<MainWindow>();

            webApp = webAppBuilder.Build();

            // Configure the HTTP request pipeline.
           webApp.UseSwagger();
           webApp.UseSwaggerUI();
           

            webApp.MapControllers();

            serviceProvider  = services.BuildServiceProvider();

        }

        protected override async void OnStartup(StartupEventArgs e)
        {

            var Edi = EdiBuilder.Create("EdiConfig.json");
            await Edi.Init();
            App.Edi = Edi;
            var mainWindos = serviceProvider.GetRequiredService<MainWindow>();
            mainWindos.Show();

            base.OnStartup(e);

            await webApp.RunAsync();
        }
    }
}
