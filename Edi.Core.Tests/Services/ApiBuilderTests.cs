using Edi.Core.Gallery;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ConfigurationManager = Edi.Core.Services.ConfigurationManager;

namespace Edi.Core.Tests.Services;

public class ApiBuilderTests
{
    [Fact]
    public async Task SelectedSiteAndUploadsCoexistAndFollowGameSelection()
    {
        var root = Path.Combine(Path.GetTempPath(), "edi-static-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var game = Path.Combine(root, "Game");
            var other = Path.Combine(root, "Other");
            var upload = Path.Combine(root, "Upload");
            foreach (var folder in new[] { game, other, upload }) Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(game, "index.html"), "player");
            await File.WriteAllTextAsync(Path.Combine(game, "module.mjs"), "export {};");
            await File.WriteAllTextAsync(Path.Combine(game, "scene.funscript"), "game script");
            await File.WriteAllTextAsync(Path.Combine(upload, "scene.funscript"), "upload script");
            await File.WriteAllTextAsync(Path.Combine(other, "index.html"), "other player");
            var config = new ConfigurationManager(Path.Combine(root, "EdiConfig.json"), Path.Combine(root, "UserConfig.json"));
            config.Get<GalleryConfig>().GalleryPath = "Game";
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton(config);
            await using var app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.UseFiles(upload);
            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            Assert.Equal("player", await client.GetStringAsync("/"));
            Assert.Equal("game script", await client.GetStringAsync("/Edi/Assets/scene.funscript"));
            Assert.Equal("upload script", await client.GetStringAsync("/Edi/Upload/scene.funscript"));
            using var module = await client.GetAsync("/module.mjs");
            Assert.Equal("text/javascript", module.Content.Headers.ContentType!.MediaType);
            using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "/Edi/Assets/scene.funscript");
            rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 3);
            using var range = await client.SendAsync(rangeRequest);
            Assert.Equal(System.Net.HttpStatusCode.PartialContent, range.StatusCode);
            config.Get<GalleryConfig>().GalleryPath = "Other";
            Assert.Equal("other player", await client.GetStringAsync("/"));
            config.Get<GalleryConfig>().GalleryPath = "Missing";
            Assert.Equal("upload script", await client.GetStringAsync("/Edi/Upload/scene.funscript"));
            await app.StopAsync();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

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
