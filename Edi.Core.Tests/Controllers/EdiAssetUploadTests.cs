using Edi.Core.Controllers;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Definition;
using Edi.Core.Players;
using Edi.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.ObjectModel;

namespace Edi.Core.Tests.Controllers;

public class EdiAssetUploadTests
{
    [Fact]
    public async Task UploadGalleryDoesNotHideSelectedGameAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), "edi-listing-tests", Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "Game");
        var upload = Path.Combine(root, "Upload");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(upload);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(game, "index.html"), "site");
            await File.WriteAllTextAsync(Path.Combine(upload, "scene.funscript"), "script");
            var config = new ConfigurationManager(Path.Combine(root, "EdiConfig.json"), Path.Combine(root, "UserConfig.json"));
            config.Get<GalleryConfig>().GalleryPath = "Game";
            var edi = new RecordingEdi();
            var controller = new EdiController(edi, upload, config);
            await using var contents = new MemoryStream("uploaded"u8.ToArray());
            await controller.CreateAssets([new FormFile(contents, 0, contents.Length, "files", "scene.funscript")]);
            Assert.Equal(upload, edi.GalleryPath);
            var files = Assert.IsType<List<string>>(Assert.IsType<OkObjectResult>(controller.Get()).Value);
            Assert.Contains("/Edi/Assets/index.html", files);
            Assert.Contains("/Edi/Upload/scene.funscript", files);
            var asset = Assert.IsType<PhysicalFileResult>(controller.GetAsset("index.html"));
            Assert.Equal(Path.Combine(game, "index.html"), asset.FileName);
            Assert.IsType<BadRequestObjectResult>(controller.GetAsset("../Game-other/index.html"));
            await controller.DeleteAssets();
            Assert.Empty(Directory.GetFiles(upload));
            Assert.Contains("/Edi/Assets/index.html", Assert.IsType<List<string>>(
                Assert.IsType<OkObjectResult>(controller.Get()).Value));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task PutAddsAssetsWithoutRemovingExistingFilesOrStoppingPlayback()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "edi-asset-upload-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var existingPath = Path.Combine(
            temporaryDirectory,
            "existing.funscript");
        await File.WriteAllTextAsync(existingPath, "existing");

        try
        {
            var edi = new RecordingEdi();
            var controller = new EdiController(edi, temporaryDirectory);
            await using var contents = new MemoryStream("new"u8.ToArray());
            var upload = new FormFile(
                contents,
                0,
                contents.Length,
                "files",
                "nested/new.funscript");

            var result = await controller.UpdateAssets([upload]);

            Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal("existing", await File.ReadAllTextAsync(existingPath));
            Assert.Equal(
                "new",
                await File.ReadAllTextAsync(
                    Path.Combine(temporaryDirectory, "new.funscript")));
            Assert.False(Directory.Exists(
                Path.Combine(temporaryDirectory, "nested")));
            Assert.Equal(0, edi.PlayerRecorder.StopCalls);
            Assert.Equal(temporaryDirectory, edi.ReloadedPath);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("scene.funscript")]
    [InlineData("scene.Stroke.funscript")]
    [InlineData("Definitions.csv")]
    [InlineData("Definitions_auto.csv")]
    [InlineData("BundleDefinition.txt")]
    [InlineData("BundleDefinition.fast.txt")]
    [InlineData("ambient.mp3")]
    public void RecognizesAssetsUsedByEdiRepositories(string fileName)
    {
        Assert.True(EdiController.IsRecognizedAssetFileName(fileName));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("Definitions.csv.exe")]
    [InlineData("scene.script")]
    [InlineData("scene.mp4")]
    [InlineData("scene.webm")]
    [InlineData("scene.avi")]
    [InlineData("scene.mkv")]
    [InlineData("scene.mov")]
    public void RejectsVideosAndUnknownFiles(string fileName)
    {
        Assert.False(EdiController.IsRecognizedAssetFileName(fileName));
    }

    private sealed class RecordingEdi : IEdi
    {
        public RecordingPlayer PlayerRecorder { get; } = new();
        public string? ReloadedPath { get; private set; }

        public Task Init(string? path = null, bool setGamePath = true)
        {
            ReloadedPath = path;
            return Task.CompletedTask;
        }

        public Task ReloadAssets(string path)
        {
            ReloadedPath = path;
            return Task.CompletedTask;
        }

        public Task InitDevices() => Task.CompletedTask;
        public bool LaunchGame(bool automatic = false) => false;
        public Task<GameInfo> SelectGame(GameInfo game) => Task.FromResult(game);
        public IPlayerChannels Player => PlayerRecorder;
        public DeviceCollector DeviceCollector => null!;
        public DeviceConfiguration DeviceConfiguration => null!;
        public IEnumerable<IRepository> repos => [];
        public ConfigurationManager ConfigurationManager => null!;
        public IEnumerable<DefinitionGallery> Definitions => [];
        public ObservableCollection<IDevice> Devices => [];
        public string GalleryPath => ReloadedPath ?? string.Empty;
        public event IEdi.ChangeStatusHandler? OnChangeStatus;
    }

    private sealed class RecordingPlayer : IPlayerChannels
    {
        public int StopCalls { get; private set; }
        public List<string> Channels { get; } = [];
        public event Action<List<string>>? ChannelsChanged;

        public void ResetChannels(List<string>? channels = null) { }
        public Task Play(string name, long seek = 0, string[]? channels = null)
            => Task.CompletedTask;
        public Task Stop(string[]? channels = null)
        {
            StopCalls++;
            return Task.CompletedTask;
        }
        public Task Pause(bool untilResume = false, string[]? channels = null)
            => Task.CompletedTask;
        public Task Resume(bool atCurrentTime = false, string[]? channels = null)
            => Task.CompletedTask;
        public Task Intensity(int max, string[]? channels = null)
            => Task.CompletedTask;
    }
}
