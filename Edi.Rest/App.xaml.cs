using Edi.Core;
using Edi.Core.Gallery;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Edi.Forms
{
    /// <summary>
    /// Interaction lógica para App.xaml
    /// </summary>
    public partial class App : Application
    {
        private ServiceProvider serviceProvider;
        private WebApplication webApp;
        public static IEdi Edi;

        public App()
        {
            Edi = EdiBuilder.Create("EdiConfig.json");

            var galleryPath = Edi.ConfigurationManager.Get<GalleryConfig>().GalleryPath;
            BuildApi(galleryPath);
        }

        private async Task BuildApi(string galleryPath)
        {
            galleryPath = new DirectoryInfo(galleryPath).FullName;

            if (webApp != null)
                await webApp.DisposeAsync();

            var webAppBuilder = WebApplication.CreateBuilder();

           
            // Configura Kestrel para escuchar en ambos puertos y especifica HTTPS
            webAppBuilder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.ListenAnyIP(5000); // Puerto HTTP
                serverOptions.ListenAnyIP(5001, listenOptions =>
                {
                    listenOptions.UseHttps(); // Utiliza el certificado de desarrollo
                });
            });
            webAppBuilder.WebHost.UseUrls("http://localhost:5000/");
            webAppBuilder.WebHost.UseUrls("https://localhost:5001/");

            IServiceCollection services = webAppBuilder.Services;

            services.AddControllersWithViews();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Edi Rest", Version = "v1" });
            });

            services.AddCors(options =>
            {
                options.AddPolicy("AllowAnyOrigin",
               builder => builder
                .AllowAnyOrigin() // Permite solicitudes desde cualquier origen
                .AllowAnyMethod()
                .AllowAnyHeader());
            });

            webApp = webAppBuilder.Build();

            webApp.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(galleryPath),
                RequestPath = "/Edi/Assets",
                ServeUnknownFileTypes = true, // Advertencia: esto podría ser un riesgo de seguridad.
                ContentTypeProvider = new FileExtensionContentTypeProvider(
                    new Dictionary<string, string>
                    {
                        { ".funscript", "application/json" }
                    })
            });

            var uploadPath = Path.Combine(Core.Edi.OutputDir, "Upload");
            if (!Directory.Exists(uploadPath))
            {
                Directory.CreateDirectory(uploadPath);
            }

            webApp.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(uploadPath),
                RequestPath = "/Edi/Upload",
                ServeUnknownFileTypes = true, // Advertencia: esto podría ser un riesgo de seguridad.
                ContentTypeProvider = new FileExtensionContentTypeProvider(
                    new Dictionary<string, string>
                    {
                        { ".funscript", "application/json" }
                    })
            });

            var distPath = Path.Combine(galleryPath, "dist");
            if (Directory.Exists(distPath))
            {
                webApp.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(distPath),
                    RequestPath = "",
                    ServeUnknownFileTypes = true, // Advertencia: esto podría ser un riesgo de seguridad.
                });
            }

            // Configura el pipeline de solicitudes HTTP
            webApp.UseSwagger();
            webApp.UseSwaggerUI();

            webApp.UseCors("AllowSpecificOrigin");

            webApp.UseRouting();
            webApp.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
                endpoints.MapRazorPages();
            });

            serviceProvider = services.BuildServiceProvider();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            var galleryPath = Edi.ConfigurationManager.Get<GalleryConfig>().GalleryPath;

            await Edi.Init(galleryPath);

            var mainWindow = new MainWindow();

            mainWindow.Show();

            base.OnStartup(e);

            // Ejecuta el servidor web en un hilo separado para no bloquear la interfaz de usuario
            Task.Run(async () => await webApp.RunAsync());
        }
    }
}
