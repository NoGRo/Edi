using Edi.Core.Controllers;
using Edi.Core.Gallery;
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

            var galleryPath = new DirectoryInfo(config.Get<GalleryConfig>().GalleryPath).FullName;

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(galleryPath),
                RequestPath = "/Edi/Assets",
                ServeUnknownFileTypes = true,
                ContentTypeProvider = new FileExtensionContentTypeProvider(new Dictionary<string, string>() { { ".funscript", "application/json" } })
            });

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(uploadPath),
                RequestPath = "/Edi/Upload",
                ServeUnknownFileTypes = true,
                ContentTypeProvider = new FileExtensionContentTypeProvider(new Dictionary<string,string>() { { ".funscript", "application/json" } })
            });

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Edi Rest v1");
                c.RoutePrefix = "swagger"; // Esto hace que sea accesible desde /swagger
            });

            app.UseCors("AllowSpecificOrigin");
            app.UseRouting();

            app.MapControllers();

            return app;
        }

    }
}
