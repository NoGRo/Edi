using Google.Protobuf;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using Proto = HdyRpc;

namespace Edi.Core.Device.Handy;

internal sealed class HandyBluetoothClient : IHandyClient
{
    private static readonly TimeSpan ResponseTimeout =
        TimeSpan.FromSeconds(5);

    private readonly IHandyBluetoothTransport _transport;
    private readonly ILogger _logger;
    private readonly Func<long> _getUnixTimeMilliseconds;
    private readonly ConcurrentDictionary<
        uint,
        TaskCompletionSource<Proto.Response>> _pending = new();
    private int _nextRequestId;
    private int _offset;
    private int _disposed;

    private HandyBluetoothClient(
        IHandyBluetoothTransport transport,
        ILogger logger,
        Func<long> getUnixTimeMilliseconds)
    {
        _transport = transport;
        _logger = logger;
        _getUnixTimeMilliseconds = getUnixTimeMilliseconds;
        _transport.FrameReceived += Transport_FrameReceived;
        _transport.Disconnected += Transport_Disconnected;
    }

    public string Id => $"bluetooth:{_transport.Id}";
    public string Key { get; private set; }
    public string DisplayName => GetDisplayName(_transport.Name);
    public TimeSpan PlaybackSyncDelay => TimeSpan.FromMilliseconds(15);

    // Keep margin below the measured 509-byte BLE payload. The initial
    // bundle and all subsequent adds use the same conservative batch.
    public int MaxPointsPerRequest => 50;
    public int MaxPlayPointsPerRequest => 50;

    public event Action<IHandyClient> Disconnected;

    public Task SynchronizeClock(CancellationToken cancellationToken)
        => SynchronizeDeviceClock(cancellationToken);

    internal static string GetDisplayName(string advertisedName)
    {
        if (advertisedName?.StartsWith(
                "OHD_hw4_",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return "The Handy 2 Pro (BLE)";
        }

        if (advertisedName?.StartsWith(
                "OHD_hw3_",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return "The Handy 2 Standard (BLE)";
        }

        return "The Handy (BLE)";
    }

    internal static async Task<HandyBluetoothClient> CreateAsync(
        IHandyBluetoothTransport transport,
        ILogger logger,
        bool initialize,
        CancellationToken cancellationToken,
        Func<long> getUnixTimeMilliseconds = null)
    {
        var client = new HandyBluetoothClient(
            transport,
            logger,
            getUnixTimeMilliseconds
                ?? (() => DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds()));
        try
        {
            if (initialize)
            {
                var keyResponse = await client.SendRequest(
                    new Proto.Request
                    {
                        RequestConnectionKeyGet =
                            new Proto.RequestConnectionKeyGet()
                    },
                    cancellationToken);
                client.Key = keyResponse.ResponseConnectionKeyGet?.Key;
            }

            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async Task<HspState> Setup(
        HspSetupRequest request,
        CancellationToken cancellationToken)
    {
        await SendRequest(
            new Proto.Request
            {
                RequestModeSet = new Proto.RequestModeSet
                {
                    Mode = Proto.Mode.Hsp
                }
            },
            cancellationToken);

        var response = await SendRequest(
            new Proto.Request
            {
                RequestHspSetup = new Proto.RequestHspSetup
                {
                    StreamId = checked((uint)request.stream_id)
                }
            },
            cancellationToken);
        return MapState(
            response.ResponseHspSetup?.State,
            "setup");
    }

    public async Task<HspState> AddPoints(
        HspAddRequest request,
        CancellationToken cancellationToken)
    {
        var response = await SendRequest(
            CreateAddRequest(request),
            cancellationToken);
        return MapState(response.ResponseHspAdd?.State, "add");
    }

    public async Task<HspState> Play(
        HspPlayRequest request,
        CancellationToken cancellationToken)
    {
        // The HTTP request model contains Handy's cloud time. BLE has
        // its own device-to-local clock synchronization, so using the
        // cloud offset here would mix two independent time domains.
        var serverTime = checked(
            _getUnixTimeMilliseconds()
            + Volatile.Read(ref _offset));
        var playRequest = new Proto.Request
        {
            RequestHspPlay = new Proto.RequestHspPlay
            {
                StartTime = request.start_time,
                ServerTime = checked((ulong)serverTime),
                PlaybackRate = Convert.ToSingle(request.playback_rate),
                Loop = request.loop,
                PauseOnStarving = true
            }
        };
        var response = request.add is null
            ? await SendRequest(playRequest, cancellationToken)
            : (await SendBundledRequests(
                [CreateAddRequest(request.add), playRequest],
                cancellationToken))[1];
        return MapState(response.ResponseHspPlay?.State, "play");
    }

    public async Task<HspState> SyncTime(
        HspSyncTimeRequest request,
        CancellationToken cancellationToken)
    {
        var serverTime = checked(
            _getUnixTimeMilliseconds()
            + Volatile.Read(ref _offset));
        var response = await SendRequest(
            new Proto.Request
            {
                RequestHspCurrentTimeSet =
                    new Proto.RequestHspCurrentTimeSet
                    {
                        CurrentTime = request.current_time,
                        ServerTime = checked((ulong)serverTime),
                        Filter = Convert.ToSingle(request.filter)
                    }
            },
            cancellationToken);
        return MapState(
            response.ResponseHspCurrentTimeSet?.State,
            "time synchronization");
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        await SendRequest(
            new Proto.Request
            {
                RequestHspStop = new Proto.RequestHspStop()
            },
            cancellationToken);
    }

    public async Task SetStroke(
        SlideRequest request,
        CancellationToken cancellationToken)
    {
        await SendRequest(
            new Proto.Request
            {
                RequestSliderStrokeSet =
                    new Proto.RequestSliderStrokeSet
                    {
                        Min = request.min / 100f,
                        Max = request.max / 100f
                    }
            },
            cancellationToken);
    }

    public Task SetOffset(
        int offset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(
            ref _offset,
            HandyConfig.NormalizeOffset(offset));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _transport.FrameReceived -= Transport_FrameReceived;
        _transport.Disconnected -= Transport_Disconnected;
        FailPending(new IOException(
            "The Handy Bluetooth client was disposed."));
        await _transport.DisposeAsync();
    }

    private async Task SynchronizeDeviceClock(
        CancellationToken cancellationToken)
    {
        var started = _getUnixTimeMilliseconds();
        var getResponse = await SendRequest(
            new Proto.Request
            {
                RequestClockOffsetGet =
                    new Proto.RequestClockOffsetGet()
            },
            cancellationToken);
        var ended = _getUnixTimeMilliseconds();
        var clock = getResponse.ResponseClockOffsetGet
            ?? throw new InvalidOperationException(
                "The Handy returned no Bluetooth clock state.");
        var roundTrip = checked((int)(ended - started));
        var midpoint = started + roundTrip / 2L;

        await SendRequest(
            new Proto.Request
            {
                RequestClockOffsetSet =
                    new Proto.RequestClockOffsetSet
                    {
                        ClockOffset = midpoint - clock.Time,
                        Rtd = roundTrip
                    }
            },
            cancellationToken);
    }

    private async Task<Proto.Response> SendRequest(
        Proto.Request request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        var id = unchecked((uint)Interlocked.Increment(
            ref _nextRequestId));
        if (id == 0)
            id = unchecked((uint)Interlocked.Increment(
                ref _nextRequestId));
        request.Id = id;
        var operation = GetOperationName(request);
        var stopwatch = Stopwatch.StartNew();

        var completion =
            new TaskCompletionSource<Proto.Response>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException(
                $"Duplicate Handy request id {id}.");
        }

        try
        {
            _logger.LogDebug(
                "Handy BLE request {RequestId} {Operation} started. Pending: {PendingCount}",
                id,
                operation,
                _pending.Count);
            var message = new Proto.RpcMessage
            {
                Type = Proto.MessageType.Request,
                Request = request
            };
            var frame = message.ToByteArray();
            await _transport.WriteAsync(frame, cancellationToken);
            var response = await completion.Task.WaitAsync(
                ResponseTimeout,
                cancellationToken);
            if (response.Error is not null)
            {
                throw new InvalidOperationException(
                    $"Handy Bluetooth error {response.Error.Code}: " +
                    response.Error.Message);
            }

            var elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            if (elapsedMilliseconds >= 250)
            {
                _logger.LogWarning(
                    "Handy BLE request {RequestId} {Operation} was slow: {ElapsedMilliseconds} ms. Pending: {PendingCount}",
                    id,
                    operation,
                    elapsedMilliseconds,
                    _pending.Count);
            }
            else
            {
                _logger.LogDebug(
                    "Handy BLE request {RequestId} {Operation} completed in {ElapsedMilliseconds} ms",
                    id,
                    operation,
                    elapsedMilliseconds);
            }

            return response;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Handy BLE request {RequestId} {Operation} was superseded after {ElapsedMilliseconds} ms",
                id,
                operation,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (TimeoutException)
        {
            _logger.LogError(
                "Handy BLE request {RequestId} {Operation} timed out after {ElapsedMilliseconds} ms",
                id,
                operation,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task<Proto.Response[]> SendBundledRequests(
        IReadOnlyList<Proto.Request> requests,
        CancellationToken cancellationToken)
    {
        var completions = requests.Select(request =>
        {
            request.Id = unchecked((uint)Interlocked.Increment(
                ref _nextRequestId));
            var completion = new TaskCompletionSource<Proto.Response>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(request.Id, completion))
                throw new InvalidOperationException(
                    $"Duplicate Handy request id {request.Id}.");
            return completion;
        }).ToArray();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug(
                "Handy BLE request bundle play with initial points started. Pending: {PendingCount}",
                _pending.Count);
            var bundled = new Proto.Requests();
            bundled.Requests_.Add(requests);
            await _transport.WriteAsync(
                new Proto.RpcMessage
                {
                    Type = Proto.MessageType.Requests,
                    Requests = bundled
                }.ToByteArray(),
                cancellationToken);
            var responses = await Task.WhenAll(completions.Select(
                completion => completion.Task.WaitAsync(
                    ResponseTimeout,
                    cancellationToken)));
            var error = responses.FirstOrDefault(response =>
                response.Error is not null)?.Error;
            if (error is not null)
                throw new InvalidOperationException(
                    $"Handy Bluetooth error {error.Code}: {error.Message}");

            _logger.LogInformation(
                "Handy BLE request bundle play with initial points completed in {ElapsedMilliseconds} ms.",
                stopwatch.ElapsedMilliseconds);
            return responses;
        }
        finally
        {
            foreach (var request in requests)
                _pending.TryRemove(request.Id, out _);
        }
    }

    private static Proto.Request CreateAddRequest(HspAddRequest request)
    {
        var add = new Proto.RequestHspAdd
        {
            Flush = request.flush,
            TailPointStreamIndex =
                checked((uint)request.tail_point_stream_index)
        };
        add.Points.Add(request.points.Select(point => new Proto.Point
        {
            T = checked((uint)point.t),
            X = checked((uint)point.x)
        }));
        return new Proto.Request { RequestHspAdd = add };
    }

    private static string GetOperationName(Proto.Request request)
    {
        if (request.RequestHspAdd is not null)
            return "HSP add/flush";
        if (request.RequestHspPlay is not null)
            return "HSP play";
        if (request.RequestHspStop is not null)
            return "HSP stop";
        if (request.RequestHspCurrentTimeSet is not null)
            return "HSP sync-time";
        if (request.RequestHspSetup is not null)
            return "HSP setup";
        if (request.RequestModeSet is not null)
            return "mode set";
        if (request.RequestClockOffsetGet is not null)
            return "clock get";
        if (request.RequestClockOffsetSet is not null)
            return "clock set";
        if (request.RequestSliderStrokeSet is not null)
            return "stroke set";
        if (request.RequestConnectionKeyGet is not null)
            return "connection initialization";

        return "unknown";
    }

    private void Transport_FrameReceived(byte[] frame)
    {
        try
        {
            var message = Proto.RpcMessage.Parser.ParseFrom(frame);
            if (message.Type == Proto.MessageType.Response
                && message.Response is not null
                && _pending.TryGetValue(
                    message.Response.Id,
                    out var completion))
            {
                completion.TrySetResult(message.Response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not decode a Handy Bluetooth Protobuf frame.");
        }
    }

    private void Transport_Disconnected()
    {
        FailPending(new IOException(
            "The Handy Bluetooth connection was lost."));
        Disconnected?.Invoke(this);
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
            completion.TrySetException(exception);
    }

    private static HspState MapState(
        Proto.HspState state,
        string operation)
    {
        if (state is null)
        {
            throw new InvalidOperationException(
                $"The Handy returned no HSP state for {operation}.");
        }

        return new HspState(
            checked((int)state.StreamId),
            checked((int)state.MaxPoints),
            checked((int)state.Points),
            state.CurrentPoint,
            state.CurrentTime,
            state.Loop,
            state.PlaybackRate,
            state.FirstPointTime,
            state.LastPointTime,
            state.PlayState switch
            {
                Proto.HspPlayState.HspStatePlaying => "playing",
                Proto.HspPlayState.HspStateStopped => "stopped",
                Proto.HspPlayState.HspStatePaused => "paused",
                Proto.HspPlayState.HspStateStarving => "starving",
                _ => "not_initialized"
            },
            state.TailPointStreamIndex,
            checked((int)state.TailPointStreamIndexThreshold));
    }

}
