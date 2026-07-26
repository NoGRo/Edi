using Edi.Core.Device.Handy;
using Edi.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace Edi.Core.Tests.Handy;

public class HandyDeviceFactoryTests
{
    [Fact]
    public async Task DetectsFirmwareFromInfoEndpoint()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            RecordingHttpMessageHandler.JsonResponse(
                """
                {
                  "fw_status": 0,
                  "fw_version": "4.1.2",
                  "fw_feature_flags": "",
                  "hw_model_no": 1,
                  "hw_model_name": "Handy",
                  "hw_model_variant": 1
                }
                """)));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        var factory = new HandyDeviceFactory(NullLogger.Instance);

        var version = await factory.DetectFirmwareVersionAsync(client);

        Assert.Equal("4.1.2", version);
        Assert.Single(handler.Requests);
        Assert.Equal("v2/info", handler.Requests[0].Path);
    }

    [Theory]
    [InlineData("4.0.0", true)]
    [InlineData("5.1.0", true)]
    [InlineData("3.9.9", false)]
    [InlineData("invalid", false)]
    [InlineData(null, false)]
    public void SelectsHspOnlyForFirmwareFourOrNewer(string? version, bool expected)
    {
        var factory = new HandyDeviceFactory(NullLogger.Instance);

        Assert.Equal(expected, factory.ShouldUseHspProtocol(version));
    }

    [Fact]
    public async Task DetectionFailureFallsBackWithoutThrowing()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(
            RecordingHttpMessageHandler.JsonResponse(
                "{}",
                HttpStatusCode.ServiceUnavailable)));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        var factory = new HandyDeviceFactory(NullLogger.Instance);

        var version = await factory.DetectFirmwareVersionAsync(client);

        Assert.Null(version);
        Assert.False(factory.ShouldUseHspProtocol(version));
    }
}
