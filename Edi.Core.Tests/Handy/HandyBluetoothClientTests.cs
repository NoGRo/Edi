using Edi.Core.Device.Handy;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Proto = HdyRpc;

namespace Edi.Core.Tests.Handy;

public class HandyBluetoothClientTests
{
    [Theory]
    [InlineData("OHD_hw4_device-id", "The Handy 2 Pro (BLE)")]
    [InlineData("ohd_HW3_device-id", "The Handy 2 Standard (BLE)")]
    [InlineData("OHD_hw2_device-id", "The Handy (BLE)")]
    [InlineData("Unknown device", "The Handy (BLE)")]
    [InlineData("", "The Handy (BLE)")]
    public void DisplayNameUsesFriendlyHardwareModel(
        string advertisedName,
        string expected)
    {
        Assert.Equal(
            expected,
            HandyBluetoothClient.GetDisplayName(advertisedName));
    }

    [Fact]
    public async Task InitializationReadsKeyAndSynchronizesClock()
    {
        var transport = new RecordingBluetoothTransport();
        var timestamps = new Queue<long>([10_000, 10_040]);
        await using var client = await HandyBluetoothClient.CreateAsync(
            transport,
            NullLogger.Instance,
            initialize: true,
            TestContext.Current.CancellationToken,
            () => timestamps.Dequeue());

        Assert.Equal("TEST-KEY", client.Key);
        Assert.Collection(
            transport.Requests,
            request => Assert.Equal(
                Proto.Request.ParamsOneofCase.RequestConnectionKeyGet,
                request.ParamsCase),
            request => Assert.Equal(
                Proto.Request.ParamsOneofCase.RequestClockOffsetGet,
                request.ParamsCase),
            request =>
            {
                Assert.Equal(
                    Proto.Request.ParamsOneofCase.RequestClockOffsetSet,
                    request.ParamsCase);
                Assert.Equal(
                    9_920,
                    request.RequestClockOffsetSet.ClockOffset);
                Assert.Equal(40, request.RequestClockOffsetSet.Rtd);
            });
    }

    [Fact]
    public async Task HspMethodsUseLocalBleClockInsteadOfHttpServerTime()
    {
        var transport = new RecordingBluetoothTransport();
        await using var client = await HandyBluetoothClient.CreateAsync(
            transport,
            NullLogger.Instance,
            initialize: false,
            CancellationToken.None,
            () => 5_000);

        var setup = await client.Setup(
            new HspSetupRequest(42),
            CancellationToken.None);
        await client.SetStroke(
            new SlideRequest(20, 80),
            CancellationToken.None);
        await client.SetOffset(15, CancellationToken.None);
        var play = await client.Play(
            new HspPlayRequest(
                start_time: 250,
                server_time: 1_000,
                playback_rate: 1,
                loop: true,
                add: new HspAddRequest(
                    [new Point(0, 10), new Point(500, 90)],
                    flush: true,
                    tail_point_stream_index: 2)),
            CancellationToken.None);
        await client.Stop(CancellationToken.None);

        Assert.Equal(42, setup.stream_id);
        Assert.Equal("playing", play.play_state);
        Assert.Collection(
            transport.Requests,
            request => Assert.Equal(
                Proto.Request.ParamsOneofCase.RequestModeSet,
                request.ParamsCase),
            request =>
            {
                Assert.Equal(
                    Proto.Request.ParamsOneofCase.RequestHspSetup,
                    request.ParamsCase);
                Assert.Equal(42u, request.RequestHspSetup.StreamId);
            },
            request =>
            {
                Assert.Equal(
                    Proto.Request.ParamsOneofCase.RequestSliderStrokeSet,
                    request.ParamsCase);
                Assert.Equal(
                    0.2f,
                    request.RequestSliderStrokeSet.Min,
                    precision: 3);
                Assert.Equal(
                    0.8f,
                    request.RequestSliderStrokeSet.Max,
                    precision: 3);
            },
            request =>
            {
                Assert.Equal(
                    Proto.Request.ParamsOneofCase.RequestHspAdd,
                    request.ParamsCase);
                Assert.True(request.RequestHspAdd.Flush);
                Assert.Equal(2u,
                    request.RequestHspAdd.TailPointStreamIndex);
                Assert.Equal(
                    [(0u, 10u), (500u, 90u)],
                    request.RequestHspAdd.Points
                        .Select(point => (point.T, point.X)));
            },
            request =>
            {
                Assert.Equal(
                    Proto.Request.ParamsOneofCase.RequestHspPlay,
                    request.ParamsCase);
                Assert.Equal(250, request.RequestHspPlay.StartTime);
                Assert.Equal(5_020uL,
                    request.RequestHspPlay.ServerTime);
                Assert.True(request.RequestHspPlay.Loop);
                Assert.True(request.RequestHspPlay.PauseOnStarving);
            },
            request => Assert.Equal(
                Proto.Request.ParamsOneofCase.RequestHspStop,
                request.ParamsCase));
    }

    [Fact]
    public void RegistrationIncludesBluetoothDiscovery()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHandy();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<HandyBluetoothDiscovery>(
            provider.GetRequiredService<IHandyBluetoothDiscovery>());
    }

    [Fact]
    public void DiscoveryRecognizesNameOrFirmwareFourService()
    {
        Assert.True(HandyBluetoothDiscovery.IsHandyAdvertisement(
            "OHD_hw4_test",
            []));
        Assert.True(HandyBluetoothDiscovery.IsHandyAdvertisement(
            string.Empty,
            [HandyBluetoothTransport.ServiceUuid]));
        Assert.False(HandyBluetoothDiscovery.IsHandyAdvertisement(
            "Other device",
            [Guid.NewGuid()]));
    }

    [Fact]
    public async Task ClientSignalsTransportDisconnect()
    {
        var transport = new RecordingBluetoothTransport();
        await using var client = await HandyBluetoothClient.CreateAsync(
            transport,
            NullLogger.Instance,
            initialize: false,
            CancellationToken.None);
        var disconnected = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += _ => disconnected.TrySetResult();

        transport.RaiseDisconnected();

        await disconnected.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
    }

    private sealed class RecordingBluetoothTransport
        : IHandyBluetoothTransport
    {
        public string Id => "test-device";
        public string Name => "OHD_hw3_test";
        public int MaxWriteSize => 512;
        public List<Proto.Request> Requests { get; } = [];

        public event Action<byte[]> FrameReceived = delegate { };
        public event Action Disconnected = delegate { };

        public void RaiseDisconnected() => Disconnected();

        public Task WriteAsync(
            byte[] frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(frame.Length <= MaxWriteSize);
            var message = Proto.RpcMessage.Parser.ParseFrom(frame);
            var request = message.Request;
            Requests.Add(request.Clone());

            var response = new Proto.Response { Id = request.Id };
            switch (request.ParamsCase)
            {
                case Proto.Request.ParamsOneofCase.RequestConnectionKeyGet:
                    response.ResponseConnectionKeyGet =
                        new Proto.ResponseConnectionKeyGet
                        {
                            Key = "TEST-KEY"
                        };
                    break;
                case Proto.Request.ParamsOneofCase.RequestClockOffsetGet:
                    response.ResponseClockOffsetGet =
                        new Proto.ResponseClockOffsetGet
                        {
                            Time = 100
                        };
                    break;
                case Proto.Request.ParamsOneofCase.RequestClockOffsetSet:
                    response.ResponseClockOffsetSet =
                        new Proto.ResponseClockOffsetSet
                        {
                            Time = 100,
                            ClockOffset =
                                request.RequestClockOffsetSet.ClockOffset,
                            Rtd = request.RequestClockOffsetSet.Rtd
                        };
                    break;
                case Proto.Request.ParamsOneofCase.RequestModeSet:
                    response.ResponseModeSet =
                        new Proto.ResponseModeSet
                        {
                            Mode = request.RequestModeSet.Mode
                        };
                    break;
                case Proto.Request.ParamsOneofCase.RequestHspSetup:
                    response.ResponseHspSetup =
                        new Proto.ResponseHspSetup
                        {
                            State = State(
                                request.RequestHspSetup.StreamId,
                                Proto.HspPlayState.HspStateStopped)
                        };
                    break;
                case Proto.Request.ParamsOneofCase
                    .RequestSliderStrokeSet:
                    response.ResponseSliderStrokeSet =
                        new Proto.ResponseSliderStrokeSet
                        {
                            Min =
                                request.RequestSliderStrokeSet.Min,
                            Max =
                                request.RequestSliderStrokeSet.Max
                        };
                    break;
                case Proto.Request.ParamsOneofCase.RequestHspAdd:
                    response.ResponseHspAdd =
                        new Proto.ResponseHspAdd
                        {
                            State = State(
                                42,
                                Proto.HspPlayState.HspStateStopped)
                        };
                    break;
                case Proto.Request.ParamsOneofCase.RequestHspPlay:
                    response.ResponseHspPlay =
                        new Proto.ResponseHspPlay
                        {
                            State = State(
                                42,
                                Proto.HspPlayState.HspStatePlaying)
                        };
                    break;
                case Proto.Request.ParamsOneofCase.RequestHspStop:
                    response.ResponseHspStop =
                        new Proto.ResponseHspStop
                        {
                            State = State(
                                42,
                                Proto.HspPlayState.HspStateStopped)
                        };
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected request {request.ParamsCase}.");
            }

            FrameReceived?.Invoke(new Proto.RpcMessage
            {
                Type = Proto.MessageType.Response,
                Response = response
            }.ToByteArray());
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static Proto.HspState State(
            uint streamId,
            Proto.HspPlayState playState)
            => new()
            {
                StreamId = streamId,
                MaxPoints = 200,
                PlayState = playState,
                PlaybackRate = 1
            };
    }
}
