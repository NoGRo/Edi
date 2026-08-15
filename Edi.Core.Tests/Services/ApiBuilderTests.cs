using Edi.Core.Gallery;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ConfigurationManager = Edi.Core.Services.ConfigurationManager;

namespace Edi.Core.Tests.Services;

public class ApiBuilderTests
{
    [Fact]
    public async Task MissingConfigAndGalleryDoNotPreventFileMiddlewareSetup()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "edi-api-builder-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var configuration = new ConfigurationManager(
                Path.Combine(temporaryDirectory, "MissingEdiConfig.json"),
                Path.Combine(temporaryDirectory, "UserConfig.json"));
            configuration.Get<GalleryConfig>().GalleryPath = Path.Combine(
                temporaryDirectory,
                "MissingGallery");

            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton(configuration);
            await using var app = builder.Build();
            var uploadPath = Path.Combine(temporaryDirectory, "Upload");

            app.UseFiles(uploadPath);

            Assert.False(File.Exists(configuration.GamePathConfig));
            Assert.False(Directory.Exists(
                configuration.Get<GalleryConfig>().GalleryPath));
            Assert.True(Directory.Exists(uploadPath));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
