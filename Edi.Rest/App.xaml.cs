using Edi.Core;
using Edi.Core.Gallery;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;
using System.IO;
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
            Edi = EdiBuilder.Create("EdiConfig.json");

            var webAppBuilder = WebApplication.CreateBuilder();
            
            webAppBuilder.WebHost.UseUrls("http://localhost:5000/");

            IServiceCollection services = webAppBuilder.Services;

            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Edi Rest", Version = "v1" });
            });

            services.AddSingleton<MainWindow>();
            services.AddCors(options =>
            {
                options.AddPolicy("AllowSpecificOrigin",
                    builder => builder.WithOrigins("http://localhost:1234", "http://localhost:5000/") // Permite solicitudes desde este origen
                                      .AllowAnyMethod()
                                      .AllowAnyOrigin()
                                      .AllowAnyHeader());
            });
            webApp = webAppBuilder.Build();

            // Configure the HTTP request pipeline.
           webApp.UseSwagger();
           webApp.UseSwaggerUI();
           
            webApp.UseCors("AllowSpecificOrigin");
            webApp.MapControllers();

            serviceProvider  = services.BuildServiceProvider();

        }

        protected override async void OnStartup(StartupEventArgs e)
        {

            var galleryPath = Edi.ConfigurationManager.Get<GalleryConfig>().GalleryPath;

            await Edi.Init(galleryPath);
            var mainWindos = serviceProvider.GetRequiredService<MainWindow>();
            webApp.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(
                   galleryPath),
                RequestPath = "/Edi/Assets",
                ServeUnknownFileTypes = true, // Advertencia: esto podría ser un riesgo de seguridad.
                ContentTypeProvider = new FileExtensionContentTypeProvider(
                    new Dictionary<string, string>
                    {
            // Añade aquí los tipos MIME personalizados.
            { ".funscript", "application/json" }
                    })
            });

            
            var distPath = galleryPath + "/dist";
            if (Directory.Exists(distPath))
            {
                webApp.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(distPath),
                    RequestPath = "",
                    ServeUnknownFileTypes = true, // Advertencia: esto podría ser un riesgo de seguridad.
                });
            }
            webApp.MapControllers();
            mainWindos.Show();

            base.OnStartup(e);

            await webApp.RunAsync();
        }
    }
}
