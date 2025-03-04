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
using Serilog;
using System.Net;
using Microsoft.Extensions.Logging;


namespace Edi.Forms
{
    /// <summary>
    /// Interaction lógica para App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static WebApplication webApp;
        public static readonly IEdi Edi = EdiBuilder.Create("EdiConfig.json");
        private string galleryPath;


        public App()
        {

            galleryPath = Edi.ConfigurationManager.Get<GalleryConfig>().GalleryPath;


            BuildApi(galleryPath);
        }

        private async Task<WebApplication> BuildApi(string galleryPath)
        {

            bool useHttps = Edi.ConfigurationManager.Get<EdiConfig>().UseHttps;

            galleryPath = new DirectoryInfo(galleryPath).FullName;

            if (webApp != null)
                await webApp.DisposeAsync();

            var webAppBuilder = WebApplication.CreateBuilder();

           
            // Configura Kestrel para escuchar en ambos puertos y especifica HTTPS
            if (useHttps)
            {
                webAppBuilder.WebHost.ConfigureKestrel(serverOptions =>
                {
                    serverOptions.Listen(IPAddress.Loopback, 5000); // Puerto HTTP
                    serverOptions.Listen(IPAddress.Loopback, 5001, listenOptions =>
                    {
                        listenOptions.UseHttps("certificate.pfx", "password"); // Utiliza el certificado de desarrollo
                    });
                });

            }
            else
            {
                webAppBuilder.WebHost.UseUrls("http://localhost:5000");
            }
            

            IServiceCollection services = webAppBuilder.Services;

            services.AddControllersWithViews();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Edi Rest", Version = "v1" });
            });

            services.AddCors(options =>
            {
                options.AddPolicy("AllowSpecificOrigin",
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

            
            return webApp;
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            await Edi.Init(galleryPath);

            var mainWindow = new MainWindow();

            mainWindow.Show();


            base.OnStartup(e);
            Task.Run(async () =>
            {
                
                await webApp.RunAsync();
            });
            Task.Run(Edi.InitDevices); 
            // Ejecuta el servidor web en un hilo separado para no bloquear la interfaz de usuario

        }
    }
}
