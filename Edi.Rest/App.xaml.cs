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
        private ServiceProvider serviceProvider;
        private  WebApplication webApp;
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

            webAppBuilder.WebHost.UseUrls("https://*:5000/");

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


            
            // Configure the HTTP request pipeline.
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


            var mainWindos = new MainWindow();
           
            
            mainWindos.Show();

            base.OnStartup(e);

            await webApp.RunAsync();
        }
    }
}
