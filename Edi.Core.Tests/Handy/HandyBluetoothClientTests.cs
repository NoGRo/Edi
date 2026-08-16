using Edi.Core.Device.Handy;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Channels;
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
    public async Task InitializationReadsKeyWithoutWaitingForClockSync()
    {
        var transport = new RecordingBluetoothTransport();
        await using var client = await HandyBluetoothClient.CreateAsync(
            transport,
            NullLogger.Instance,
            initialize: true,
            TestContext.Current.CancellationToken,
            () => 10_000);

        Assert.Equal("TEST-KEY", client.Key);
        var request = Assert.Single(transport.Requests);
        Assert.Equal(
            Proto.Request.ParamsOneofCase.RequestConnectionKeyGet,
            request.ParamsCase);
    }

    [Fact]
    public async Task ClockSynchronizationUsesBleTimingProtocol()
    {
        var transport = new RecordingBluetoothTransport();
        var timestamps = new Queue<long>([10_000, 10_040]);
        await using var client = await HandyBluetoothClient.CreateAsync(
            transport,
            NullLogger.Instance,
            initialize: false,
            TestContext.Current.CancellationToken,
            () => timestamps.Dequeue());

        await client.SynchronizeClock(
            TestContext.Current.CancellationToken);

        Assert.Collection(
            transport.Requests,
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
    public async Task HspMethodsUseLocalBleClockAndShortSyncDelay()
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
        var synchronized = await client.SyncTime(
            new HspSyncTimeRequest(
                current_time: 750,
                server_time: 1_000,
                filter: 1),
            CancellationToken.None);
        await client.Stop(CancellationToken.None);

        Assert.Equal(42, setup.stream_id);
        Assert.Equal("playing", play.play_state);
        Assert.Equal("playing", synchronized.play_state);
        Assert.Equal(
            TimeSpan.FromMilliseconds(15),
            client.PlaybackSyncDelay);
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
            request =>
            {
                Assert.Equal(
                    Proto.Request.ParamsOneofCase
                        .RequestHspCurrentTimeSet,
                    request.ParamsCase);
                Assert.Equal(
                    750,
                    request.RequestHspCurrentTimeSet.CurrentTime);
                Assert.Equal(
                    5_020uL,
                    request.RequestHspCurrentTimeSet.ServerTime);
                Assert.Equal(
                    1f,
                    request.RequestHspCurrentTimeSet.Filter);
            },
            request => Assert.Equal(
                Proto.Request.ParamsOneofCase.RequestHspStop,
                request.ParamsCase));
    }

    [Fact]
    public async Task TunedHspAddLimitFitsNegotiatedBlePayload()
    {
        var transport = new RecordingBluetoothTransport(maxWriteSize: 509);
        await using var client = await HandyBluetoothClient.CreateAsync(
            transport,
            NullLogger.Instance,
            initialize: false,
            CancellationToken.None);
        Assert.Equal(50, client.MaxPointsPerRequest);
        var points = Enumerable.Range(0, client.MaxPointsPerRequest)
            .Select(index => new Point(190_000 + index, 100))
            .ToList();

        await client.AddPoints(
            new HspAddRequest(
                points,
                flush: true,
                tail_point_stream_index: points.Count),
            TestContext.Current.CancellationToken);

        var add = Assert.Single(transport.Requests).RequestHspAdd;
        Assert.Equal(points.Count, add.Points.Count);
        Assert.True(add.Flush);
        Assert.Equal(points.Count, checked((int)add.TailPointStreamIndex));
        Assert.InRange(
            Assert.Single(transport.FrameLengths),
            1,
            transport.MaxWriteSize);
    }

    [Fact]
    public async Task ConfiguredBundledPlayBatchFitsNegotiatedBlePayload()
    {
        var transport = new RecordingBluetoothTransport(
            maxWriteSize: 509);
        await using var client = await HandyBluetoothClient.CreateAsync(
            transport,
            NullLogger.Instance,
            initialize: false,
            CancellationToken.None);

        static HspPlayRequest PlayWithPoints(int count) => new(
            start_time: 2_000_000,
            server_time: 0,
            playback_rate: 1,
            loop: true,
            add: new HspAddRequest(
                Enumerable.Range(0, count)
                    .Select(index => new Point(
                        2_000_000 + index,
                        100))
                    .ToList(),
                flush: true,
                tail_point_stream_index: count));

        await client.Play(
            PlayWithPoints(client.MaxPlayPointsPerRequest),
            CancellationToken.None);
        Assert.Equal(50, client.MaxPlayPointsPerRequest);
        Assert.Equal(2, transport.Requests.Count);
        Assert.InRange(
            Assert.Single(transport.FrameLengths),
            1,
            509);
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

    [Fact]
    public async Task IsolatedMissingResponseKeepsClientAvailable()
    {
        var transport = new RecordingBluetoothTransport
        {
            SuppressResponses = true
        };
        await using var client = await HandyBluetoothClient.CreateAsync(
            transport,
            NullLogger.Instance,
            initialize: false,
            CancellationToken.None,
            responseTimeout: TimeSpan.Zero);
        var disconnected = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += _ => disconnected.TrySetResult();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            client.Stop(TestContext.Current.CancellationToken));

        Assert.False(disconnected.Task.IsCompleted);
        transport.SuppressResponses = false;
        await client.Stop(TestContext.Current.CancellationToken);

        Assert.False(disconnected.Task.IsCompleted);
        Assert.Equal(2, transport.Requests.Count);
    }

    [Fact]
    public async Task RepeatedMissingResponsesSignalDisconnectForRecovery()
    {
        var transport = new RecordingBluetoothTransport
        {
            SuppressResponses = true
        };
        await using var client = await HandyBluetoothClient.CreateAsync(
            transport,
            NullLogger.Instance,
            initialize: false,
            CancellationToken.None,
            responseTimeout: TimeSpan.Zero);
        var disconnected = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += _ => disconnected.TrySetResult();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await Assert.ThrowsAsync<TimeoutException>(() =>
                client.Stop(TestContext.Current.CancellationToken));
            Assert.Equal(
                attempt == 3,
                disconnected.Task.IsCompleted);
        }

        await disconnected.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DiscoveryStopsAfterNoNewHandyAppears()
    {
        var newDevices = Channel.CreateUnbounded<bool>();
        var delays = new ControlledDelays();
        newDevices.Writer.TryWrite(true);

        var scan = HandyBluetoothDiscovery.WaitForDiscoveryWindowAsync(
            newDevices.Reader,
            TimeSpan.FromSeconds(8),
            1,
            TestContext.Current.CancellationToken,
            delays.Delay);

        var timeout = await delays.NextRequest();
        Assert.Equal(TimeSpan.FromSeconds(8), timeout.Duration);
        var firstQuietPeriod = await delays.NextRequest();
        Assert.Equal(
            TimeSpan.FromMilliseconds(750),
            firstQuietPeriod.Duration);

        newDevices.Writer.TryWrite(true);
        var secondQuietPeriod = await delays.NextRequest();
        Assert.Equal(
            TimeSpan.FromMilliseconds(750),
            secondQuietPeriod.Duration);
        Assert.False(scan.IsCompleted);

        secondQuietPeriod.Complete();
        await scan;

        Assert.True(timeout.CancellationToken.IsCancellationRequested);
        Assert.True(
            firstQuietPeriod.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task DiscoveryWaitsForEveryExpectedHandy()
    {
        var newDevices = Channel.CreateUnbounded<bool>();
        var delays = new ControlledDelays();
        newDevices.Writer.TryWrite(true);

        var scan = HandyBluetoothDiscovery.WaitForDiscoveryWindowAsync(
            newDevices.Reader,
            TimeSpan.FromSeconds(8),
            2,
            TestContext.Current.CancellationToken,
            delays.Delay);

        var timeout = await delays.NextRequest();
        Assert.Equal(TimeSpan.FromSeconds(8), timeout.Duration);
        Assert.Equal(1, delays.RequestCount);
        Assert.False(scan.IsCompleted);

        newDevices.Writer.TryWrite(true);
        var quietPeriod = await delays.NextRequest();
        Assert.Equal(
            TimeSpan.FromMilliseconds(750),
            quietPeriod.Duration);

        quietPeriod.Complete();
        await scan;
    }

    private sealed class RecordingBluetoothTransport
        : IHandyBluetoothTransport
    {
        private readonly int _maxWriteSize;

        public RecordingBluetoothTransport(int maxWriteSize = 512)
        {
            _maxWriteSize = maxWriteSize;
        }

        public string Id => "test-device";
        public string Name => "OHD_hw3_test";
        public int MaxWriteSize => _maxWriteSize;
        public List<Proto.Request> Requests { get; } = [];
        public List<int> FrameLengths { get; } = [];
        public bool SuppressResponses { get; set; }

        public event Action<byte[]> FrameReceived = delegate { };
        public event Action Disconnected = delegate { };

        public void RaiseDisconnected() => Disconnected();

        public Task WriteAsync(
            byte[] frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(frame.Length <= MaxWriteSize);
            FrameLengths.Add(frame.Length);
            var message = Proto.RpcMessage.Parser.ParseFrom(frame);
            var frameRequests = message.Type == Proto.MessageType.Requests
                ? message.Requests.Requests_
                : [message.Request];
            foreach (var request in frameRequests)
            {
                Requests.Add(request.Clone());

                if (SuppressResponses)
                    continue;

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
                case Proto.Request.ParamsOneofCase
                    .RequestHspCurrentTimeSet:
                    response.ResponseHspCurrentTimeSet =
                        new Proto.ResponseHspCurrentTimeSet
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
            }
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

    private sealed class ControlledDelays
    {
        private readonly Channel<DelayRequest> _requests =
            Channel.CreateUnbounded<DelayRequest>();
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public Task Delay(
            TimeSpan duration,
            CancellationToken cancellationToken)
        {
            var request = new DelayRequest(
                duration,
                cancellationToken);
            Interlocked.Increment(ref _requestCount);
            _requests.Writer.TryWrite(request);
            return request.Task;
        }

        public Task<DelayRequest> NextRequest()
            => _requests.Reader.ReadAsync(
                    TestContext.Current.CancellationToken)
                .AsTask();
    }

    private sealed class DelayRequest
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DelayRequest(
            TimeSpan duration,
            CancellationToken cancellationToken)
        {
            Duration = duration;
            CancellationToken = cancellationToken;
            cancellationToken.Register(
                () => _completion.TrySetCanceled(cancellationToken));
        }

        public TimeSpan Duration { get; }
        public CancellationToken CancellationToken { get; }
        public Task Task => _completion.Task;

        public void Complete() => _completion.TrySetResult();
    }
}
