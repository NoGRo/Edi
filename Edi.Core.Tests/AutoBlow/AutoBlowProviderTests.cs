using Edi.Core.Device.AutoBlow;
using Edi.Core.Tests.Support;
using Newtonsoft.Json.Linq;
using System.Net;

namespace Edi.Core.Tests.AutoBlow;

public class AutoBlowProviderTests
{
    private const string Key = "TESTTOKEN123";

    [Theory]
    [InlineData(false, "autoblow")]
    [InlineData(true, "vacuglide")]
    public void ClientUsesExpectedApiAndSharedKey(
        bool isVacuGlide,
        string api)
    {
        using var client = AutoBlowProvider.NewClient(
            Key,
            "cluster.test",
            isVacuGlide);

        Assert.Equal(
            $"https://cluster.test/{api}/",
            client.BaseAddress!.AbsoluteUri);
        Assert.Equal(
            Key,
            Assert.Single(
                client.DefaultRequestHeaders.GetValues("x-device-token")));
    }

    [Fact]
    public async Task DiscoveryFallsBackToVacuGlideApi()
    {
        HttpClient CreateClient(
            string key,
            string? cluster,
            bool isVacuGlide)
            => AutoBlowProvider.NewClient(
                key,
                cluster,
                isVacuGlide,
                new CallbackHandler(request => Json(
                    request.RequestUri!.AbsolutePath == "/vacuglide/connected"
                        ? """{"connected":true,"cluster":"device.test"}"""
                        : """{"connected":false}""")));

        var device = await AutoBlowProvider.DiscoverAsync(Key, CreateClient);

        Assert.NotNull(device);
        Assert.True(device.IsVacuGlide);
        Assert.Equal(
            "https://device.test/vacuglide/",
            device.Client.BaseAddress!.AbsoluteUri);
        device.Client.Dispose();
    }

    [Fact]
    public async Task OffsetUsesSharedConfigValue()
    {
        var handler = new RecordingHttpMessageHandler();
        using var client = AutoBlowProvider.NewClient(
            Key,
            "offset.test",
            true,
            handler);

        await AutoBlowDevice.ApplyOffset(
            client,
            125,
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("vacuglide/sync-script/offset", request.Path);
        Assert.Equal(
            130,
            JObject.Parse(request.Content!).Value<int>("offsetTimeMs"));
    }

    [Fact]
    public async Task UploadRetriesTransientFailures()
    {
        var attempts = 0;
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(
                RecordingHttpMessageHandler.JsonResponse(
                    "{}",
                    ++attempts < 3
                        ? HttpStatusCode.ServiceUnavailable
                        : HttpStatusCode.OK)));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://device.test/")
        };

        var uploaded = await AutoBlowDevice.UploadBundleWithRetry(
            client,
            () => new MemoryStream([1, 2, 3]),
            "bundle.csv",
            TestContext.Current.CancellationToken,
            retryDelay: (_, _) => Task.CompletedTask);

        Assert.True(uploaded);
        Assert.Equal(3, handler.Requests.Count);
    }

    private static HttpResponseMessage Json(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        };

    private sealed class CallbackHandler(
        Func<HttpRequestMessage, HttpResponseMessage> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(callback(request));
    }
}
