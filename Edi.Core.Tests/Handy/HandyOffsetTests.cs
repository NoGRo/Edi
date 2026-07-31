using Edi.Core.Device.Handy;
using Edi.Core.Device;
using Edi.Core.Services;
using Edi.Core.Tests.Support;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edi.Core.Tests.Handy;

public class HandyOffsetTests
{
    [Theory]
    [InlineData(-8000, -2000)]
    [InlineData(-4000, -2000)]
    [InlineData(-14, -10)]
    [InlineData(-5, -10)]
    [InlineData(4, 0)]
    [InlineData(5, 10)]
    [InlineData(4000, 2000)]
    [InlineData(8000, 2000)]
    public void ConfigNormalizesOffsetToAllowedTenMillisecondSteps(
        int input,
        int expected)
    {
        var config = new HandyConfig
        {
            OffsetMS = input
        };

        Assert.Equal(expected, config.OffsetMS);
    }

    [Fact]
    public void FodyNotifiesWhenOffsetChanges()
    {
        var config = new HandyConfig();
        string? changedProperty = null;
        ((INotifyPropertyChanged)config).PropertyChanged +=
            (_, args) => changedProperty = args.PropertyName;

        config.OffsetMS = 250;

        Assert.Equal(nameof(HandyConfig.OffsetMS), changedProperty);
    }

    [Theory]
    [InlineData(false, "v2/hstp/offset")]
    [InlineData(true, "v3/hstp/offset")]
    public async Task SendsUserOffsetToVersionSpecificHstpEndpoint(
        bool usesV3Api,
        string expectedPath)
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(
                RecordingHttpMessageHandler.JsonResponse("{}")));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };

        await HandyProvider.ApplyOffset(
            client,
            usesV3Api,
            125,
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(expectedPath, request.Path);
        Assert.Equal(
            130,
            JObject.Parse(request.Content!).Value<int>("offset"));
    }

    [Fact]
    public async Task OffsetPersistsInUserConfiguration()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "edi-handy-offset-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var gameConfigPath = Path.Combine(
                temporaryDirectory,
                "EdiConfig.json");
            var userConfigPath = Path.Combine(
                temporaryDirectory,
                "UserConfig.json");
            await File.WriteAllTextAsync(
                gameConfigPath,
                "{}",
                TestContext.Current.CancellationToken);

            var manager = new ConfigurationManager(
                gameConfigPath,
                userConfigPath);
            var config = manager.Get<HandyConfig>();

            config.OffsetMS = 340;

            var saved = JObject.Parse(
                await File.ReadAllTextAsync(
                    userConfigPath,
                    TestContext.Current.CancellationToken));
            Assert.Equal(
                340,
                saved["Handy"]!.Value<int>("OffsetMS"));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DeviceConfigurationAppliesItsOwnOffset()
    {
        var offsetApplied = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHttpMessageHandler((_, _) =>
        {
            offsetApplied.TrySetResult();
            return Task.FromResult(
                RecordingHttpMessageHandler.JsonResponse("{}"));
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add(
            "X-Connection-Key",
            "TEST-KEY");
        var device = new HandyDevice(
            client,
            repository: null!,
            NullLogger.Instance);
        var configuration = new DeviceConfig
        {
            OffsetMS = 125
        };

        device.ApplyConfiguration(configuration);
        await offsetApplied.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(130, device.OffsetMilliseconds);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("v2/hstp/offset", request.Path);
        Assert.Equal(
            130,
            JObject.Parse(request.Content!).Value<int>("offset"));
    }

    [Fact]
    public async Task ChangingDeviceOffsetAppliesTheNewValueFromTheDevice()
    {
        var secondOffsetApplied = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;
        var handler = new RecordingHttpMessageHandler((_, _) =>
        {
            if (Interlocked.Increment(ref requestCount) == 2)
                secondOffsetApplied.TrySetResult();

            return Task.FromResult(
                RecordingHttpMessageHandler.JsonResponse("{}"));
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add(
            "X-Connection-Key",
            "TEST-KEY");
        var device = new HandyDevice(
            client,
            repository: null!,
            NullLogger.Instance);

        var configuration = new DeviceConfig { OffsetMS = 100 };
        device.ApplyConfiguration(configuration);
        await device.OffsetUpdate.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        configuration.OffsetMS = 245;
        await secondOffsetApplied.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);

        Assert.Equal(250, device.OffsetMilliseconds);
        var request = handler.Requests.Last();
        Assert.Equal("v2/hstp/offset", request.Path);
        Assert.Equal(
            250,
            JObject.Parse(request.Content!).Value<int>("offset"));
    }
}
