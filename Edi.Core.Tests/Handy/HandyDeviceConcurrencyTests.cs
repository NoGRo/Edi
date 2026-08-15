using Edi.Core.Device.Handy;
using Edi.Core.Funscript.FileJson;
using Edi.Core.Gallery.Index;
using Edi.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace Edi.Core.Tests.Handy;

public class HandyDeviceConcurrencyTests
{
    [Fact]
    public async Task DisabledBundlerIsReadyUntilAPlayStartsLoadingItsScript()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        rig.Configuration.Get<GalleryBundlerConfig>().DisableBundler = true;
        var repository = CreateIndexRepository(rig);
        var gallery = new IndexGallery
        {
            Name = "scene",
            Variant = "default",
            Bundle = "scene"
        };
        AddGallery(repository, gallery);

        var handler = new RecordingHttpMessageHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add("X-Connection-Key", "TEST-KEY");
        var device = new HandyDevice(
            client,
            repository,
            NullLogger.Instance);

        Assert.True(device.IsReady);

        device.SelectedVariant = "default";

        Assert.True(device.IsReady);

#pragma warning disable xUnit1051 // The public device API does not accept a cancellation token.
        await device.PlayGallery(gallery).WaitAsync(
            TestContext.Current.CancellationToken);
#pragma warning restore xUnit1051

        Assert.False(device.IsReady);
    }

    [Fact]
    public async Task StopCancelsWarmupAndPreventsSecondPlayRequest()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var repository = CreateIndexRepository(rig);
        AddGallery(repository, new IndexGallery
        {
            Name = "scene",
            Variant = "default",
            Bundle = "default",
            Duration = 5000,
            Loop = true,
            Actions =
            [
                new FunScriptAction { at = 0, pos = 0 },
                new FunScriptAction { at = 5000, pos = 100 }
            ]
        });

        var handler = new RecordingHttpMessageHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add("X-Connection-Key", "TEST-KEY");

        var warmupStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var warmupCancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async Task ControlledDelay(TimeSpan _, CancellationToken cancellationToken)
        {
            warmupStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                warmupCancelled.TrySetResult();
                throw;
            }
        }

        var device = new HandyDevice(
            client,
            repository,
            NullLogger.Instance,
            ControlledDelay)
        {
            IsReady = true
        };
        device.selectedVariant = "default";

        var playback = device.PlayGallery("scene");
        await warmupStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        await device.Stop();
        await warmupCancelled.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);
        await playback.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Single(handler.Requests, request => request.Path == "v2/hssp/play");
        Assert.Single(handler.Requests, request => request.Path == "v2/hssp/stop");
    }

    private static IndexRepository CreateIndexRepository(PlayerTestRig rig)
    {
        var bundler = new GalleryBundler(rig.Configuration);
        return new IndexRepository(
            rig.Configuration,
            bundler,
            rig.Funscripts,
            rig.Definitions);
    }

    private static void AddGallery(IndexRepository repository, IndexGallery gallery)
    {
        var galleries = new Dictionary<string, Dictionary<string, List<IndexGallery>>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = new Dictionary<string, List<IndexGallery>>(
                StringComparer.OrdinalIgnoreCase)
            {
                [gallery.Name] = [gallery]
            }
        };

        var property = typeof(IndexRepository).GetProperty(
            "Galleries",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("IndexRepository.Galleries was not found.");
        property.SetValue(repository, galleries);
    }
}
