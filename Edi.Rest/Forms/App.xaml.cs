using Edi.Core;
using Microsoft.OpenApi.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Windows;

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
            IServiceCollection services = webAppBuilder.Services;


            services.AddEdi();
            
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

            var Edi = serviceProvider.GetRequiredService<IEdi>();
            await Edi.Init();
            App.Edi = Edi;
            var mainWindos = serviceProvider.GetRequiredService<MainWindow>();
            mainWindos.Show();

            base.OnStartup(e);

            await webApp.RunAsync();
        }
    }
}
