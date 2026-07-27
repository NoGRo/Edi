using Edi.Core.Funscript.Command;
using Edi.Core.Funscript.FileJson;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.EStimAudio;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Gallery.Index;
using Edi.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edi.Core.Tests.Gallery;

public class RepositoryManagerTests
{
    [Fact]
    public void ResolvingProvidersDoesNotCreateDeviceRepositories()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "edi-provider-resolution-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddEdi(
                Path.Combine(temporaryDirectory, "EdiConfig.json"));

            using var serviceProvider = services.BuildServiceProvider();
            var manager =
                serviceProvider.GetRequiredService<RepositoryManager>();

            Assert.NotEmpty(
                serviceProvider.GetServices<IDeviceProvider>());
            Assert.True(manager.IsCreated<DefinitionRepository>());
            Assert.False(manager.IsCreated<FunscriptRepository>());
            Assert.False(manager.IsCreated<IndexRepository>());
            Assert.False(manager.IsCreated<AudioRepository>());
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CreatesAndInitializesOnlyTheRequestedRepository()
    {
        var firstDirectory = CreateGalleryDirectory();
        var secondDirectory = CreateGalleryDirectory();

        try
        {
            var configPath = Path.Combine(firstDirectory, "EdiConfig.json");
            var userConfigPath = Path.Combine(firstDirectory, "UserConfig.json");
            var configuration = new ConfigurationManager(configPath, userConfigPath);
            var definitions = new DefinitionRepository(configuration);

            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddSingleton(definitions);
            services.AddSingleton<ILogger<FunscriptRepository>>(
                NullLogger<FunscriptRepository>.Instance);
            services.AddSingleton<ILogger<AudioRepository>>(
                NullLogger<AudioRepository>.Instance);

            using var serviceProvider = services.BuildServiceProvider();
            var manager = new RepositoryManager(serviceProvider, definitions);

            await manager.ChangePath(firstDirectory);

            Assert.True(manager.IsCreated<DefinitionRepository>());
            Assert.False(manager.IsCreated<FunscriptRepository>());
            Assert.False(manager.IsCreated<IndexRepository>());
            Assert.False(manager.IsCreated<AudioRepository>());

            var funscripts =
                await manager.GetRepositoryAsync<FunscriptRepository>();

            Assert.NotNull(funscripts.Get("hit", "default"));
            Assert.True(manager.IsCreated<FunscriptRepository>());
            Assert.False(manager.IsCreated<IndexRepository>());
            Assert.False(manager.IsCreated<AudioRepository>());

            File.Delete(Path.Combine(secondDirectory, "hit.funscript"));
            await manager.ChangePath(secondDirectory);

            Assert.Null(funscripts.Get("hit", "default"));
            Assert.False(manager.IsCreated<IndexRepository>());
            Assert.False(manager.IsCreated<AudioRepository>());
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
        }
    }

    [Fact]
    public void GalleryBundlerClearReleasesPreviouslyAddedGalleries()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "edi-bundler-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var configuration = new ConfigurationManager(
                Path.Combine(temporaryDirectory, "EdiConfig.json"),
                Path.Combine(temporaryDirectory, "UserConfig.json"));
            var bundler = new GalleryBundler(configuration);

            bundler.Clear();
            bundler.Add(CreateFunscriptGallery("first"), "default");
            Assert.Equal(1, bundler.RetainedGalleryCount);

            bundler.Clear();

            Assert.Equal(0, bundler.RetainedGalleryCount);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static string CreateGalleryDirectory()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "edi-repository-manager-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        var fixtureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "Galleries");
        foreach (var sourcePath in Directory.EnumerateFiles(fixtureDirectory))
        {
            File.Copy(
                sourcePath,
                Path.Combine(temporaryDirectory, Path.GetFileName(sourcePath)));
        }

        return temporaryDirectory;
    }

    private static FunscriptGallery CreateFunscriptGallery(string name)
    {
        var builder = new ScriptBuilder();
        builder.AddCommandMillis(100, 50);
        return new FunscriptGallery
        {
            Name = name,
            Variant = "default",
            Duration = 100,
            AxesCommands =
            {
                [Axis.Default] = builder.Generate()
            }
        };
    }
}
