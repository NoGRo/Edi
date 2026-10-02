using Edi.Core.Controllers;
using Edi.Core.Controllers.Parameters;
using Edi.Core.Gallery;
using Edi.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi.Models;
using System.IO;
using System.Net;

namespace Edi.Core
{
    public static class ApiBuilder
    {

        public static WebApplication BuildApi(ConfigurationManager config, IEdi edi)
        {
            var uploadPath = Path.Combine(Edi.OutputDir, "Upload");
            Directory.CreateDirectory(uploadPath);

            var builder = WebApplication.CreateBuilder();

            var useHttps = config.Get<EdiConfig>().UseHttps;

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.Listen(IPAddress.Loopback, 5000);
                if (useHttps)
                    serverOptions.Listen(IPAddress.Loopback, 5001, listenOptions =>
                        listenOptions.UseHttps("certificate.pfx", "password"));
            });


            var services = builder.Services;
            services.AddSingleton(config);
            services.AddSingleton(edi);

            //services.AddControllersWithViews();
            services.AddControllers().AddApplicationPart(typeof(EdiController).Assembly);
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Edi Rest", Version = "v1" });
                c.OperationFilter<SwaggerChannelsParameterOperationFilter>();
                c.EnableAnnotations(); // Enable Swagger annotations for summaries and descriptions
            });


            services.AddCors(options =>
            {
                options.AddPolicy("AllowSpecificOrigin",
                    builder => builder.AllowAnyOrigin()
                                      .AllowAnyMethod()
                                      .AllowAnyHeader());
            });
            var app = builder.Build();

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Edi Rest v1");
                c.RoutePrefix = "swagger"; // Esto hace que sea accesible desde /swagger
            });

            app.UseCors("AllowSpecificOrigin");
            app.UseFiles();
            app.UseRouting();

            app.MapControllers();
            return app;
        }





        public static void UseFiles(this WebApplication app)
            => app.UseFiles(Path.Combine(Edi.OutputDir, "Upload"));

        internal static void UseFiles(
            this WebApplication app,
            string uploadPath)
        {

            var config = app.Services.GetRequiredService<ConfigurationManager>();
            Directory.CreateDirectory(uploadPath);
            // Resolve the selected game on every request: uploads change repository state,
            // and WPF can select a different game without restarting the API.
            app.Use(async (context, next) =>
            {
                var galleryPath = ConfiguredGalleryPath(config);
                using var gallery = Directory.Exists(galleryPath)
                    ? new PhysicalFileProvider(galleryPath) : null;
                using var uploads = new PhysicalFileProvider(uploadPath);
                var files = ((IApplicationBuilder)app).New();
                var types = new FileExtensionContentTypeProvider();
                types.Mappings[".funscript"] = "application/json";
                types.Mappings[".mjs"] = "text/javascript";
                files.UseStaticFiles(new StaticFileOptions {
                    FileProvider = uploads, RequestPath = "/Edi/Upload",
                    ServeUnknownFileTypes = true, ContentTypeProvider = types });
                if (gallery != null)
                {
                    files.UseStaticFiles(new StaticFileOptions {
                        FileProvider = gallery, RequestPath = "/Edi/Assets",
                        ServeUnknownFileTypes = true, ContentTypeProvider = types });
                    files.UseDefaultFiles(new DefaultFilesOptions { FileProvider = gallery });
                    files.UseStaticFiles(new StaticFileOptions {
                        FileProvider = gallery, ContentTypeProvider = types });
                }
                files.Run(next);
                await files.Build()(context);
            });
        }

        internal static string ConfiguredGalleryPath(ConfigurationManager config)
        {
            var path = config.Get<GalleryConfig>().GalleryPath;
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(config.GamePathConfig))!, path));
        }
    }
}
