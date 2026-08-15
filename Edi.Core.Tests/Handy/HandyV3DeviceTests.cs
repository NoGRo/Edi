using Edi.Core.Device.Handy;
using Edi.Core.Funscript.Command;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Reflection;

namespace Edi.Core.Tests.Handy;

public class HandyV3DeviceTests
{
    [Fact]
    public async Task LongLoopStartsWithPlayAddThenStreamsRemainingPoints()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var repository = rig.Funscripts;
        var actions = Enumerable.Range(0, 160)
            .Select(index => new CmdLinear
            {
                AbsoluteTime = index * 50,
                Value = index % 2 == 0 ? 0 : 100
            })
            .ToList();
        AddGallery(repository, new FunscriptGallery
        {
            Name = "loop",
            Variant = "default",
            Duration = Convert.ToInt32(actions[^1].AbsoluteTime),
            Loop = true,
            Commands = actions
        });

        var streamedAddRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHttpMessageHandler(async (request, token) =>
        {
            var isAdd =
                request.RequestUri?.AbsolutePath.EndsWith("/hsp/add") == true;
            if (isAdd)
                await streamedAddRelease.Task.WaitAsync(token);

            var isSetup =
                request.RequestUri?.AbsolutePath.EndsWith("/hsp/setup") == true;
            return RecordingHttpMessageHandler.JsonResponse(
                isSetup
                    ? HspStateJson(points: 0, maxPoints: 200, tail: 0)
                    : HspStateJson(points: 160, maxPoints: 200, tail: 160));
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add("X-Connection-Key", "TEST-KEY");

        var device = new HandyV3Device(
            new HandyHttpClient(client),
            repository,
            NullLogger.Instance);
        device.selectedVariant = "default";

        await device.PlayGallery("loop");
        await handler.WaitForPathAsync("v3/hsp/play");
        await handler.WaitForPathAsync("v3/hsp/add");

        Assert.Equal(
            ["v3/hsp/setup", "v3/hsp/play", "v3/hsp/add"],
            handler.Requests
                .Where(request => request.Path.StartsWith("v3/hsp/"))
                .Select(request => request.Path));

        var hspRequests = handler.Requests
            .Where(request => request.Path.StartsWith("v3/hsp/"))
            .ToList();

        var play = JObject.Parse(hspRequests[1].Content!);
        Assert.True(play.Value<bool>("loop"));
        Assert.True(play["add"]!.Value<bool>("flush"));
        Assert.Equal(100, play["add"]!["points"]!.Count());
        Assert.Equal(
            100,
            play["add"]!.Value<int>("tail_point_stream_index"));

        var streamedAdd = JObject.Parse(hspRequests[2].Content!);
        Assert.False(streamedAdd.Value<bool>("flush"));
        Assert.Equal(60, streamedAdd["points"]!.Count());
        Assert.Equal(
            160,
            streamedAdd.Value<int>("tail_point_stream_index"));

        streamedAddRelease.SetResult();
        await device.Stop();
    }

    [Fact]
    public async Task LoopSeekStartsWithOneRotatedPlayChunk()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var repository = rig.Funscripts;
        var actions = Enumerable.Range(0, 160)
            .Select(index => new CmdLinear
            {
                AbsoluteTime = index * 100,
                Value = index % 2 == 0 ? 0 : 100
            })
            .ToList();
        AddGallery(repository, new FunscriptGallery
        {
            Name = "seeked-loop",
            Variant = "default",
            Duration = Convert.ToInt32(actions[^1].AbsoluteTime),
            Loop = true,
            Commands = actions
        });

        var remainingUploadRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHttpMessageHandler(async (request, token) =>
        {
            var isRemainingAdd =
                request.RequestUri?.AbsolutePath.EndsWith("/hsp/add") == true;
            if (isRemainingAdd)
                await remainingUploadRelease.Task.WaitAsync(token);

            var isSetup =
                request.RequestUri?.AbsolutePath.EndsWith("/hsp/setup") == true;
            return RecordingHttpMessageHandler.JsonResponse(
                isSetup
                    ? HspStateJson(points: 0, maxPoints: 200, tail: 0)
                    : HspStateJson(points: 100, maxPoints: 200, tail: 100));
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add("X-Connection-Key", "TEST-KEY");

        var device = new HandyV3Device(
            new HandyHttpClient(client),
            repository,
            NullLogger.Instance);
        device.selectedVariant = "default";

        await device.PlayGallery("seeked-loop", seek: 12_000);
        await handler.WaitForPathAsync("v3/hsp/add");

        var hspRequests = handler.Requests
            .Where(request => request.Path.StartsWith("v3/hsp/"))
            .ToList();
        Assert.Equal(
            ["v3/hsp/setup", "v3/hsp/play", "v3/hsp/add"],
            hspRequests.Take(3).Select(request => request.Path));

        var play = JObject.Parse(hspRequests[1].Content!);
        Assert.Equal(12_000, play.Value<int>("start_time"));
        Assert.True(play["add"]!.Value<bool>("flush"));
        Assert.Equal(100, play["add"]!["points"]!.Count());
        Assert.Equal(
            11_900,
            play["add"]!["points"]!.First()!.Value<int>("t"));
        Assert.Equal(
            21_700,
            play["add"]!["points"]!.Last()!.Value<int>("t"));

        var remaining = JObject.Parse(hspRequests[2].Content!);
        Assert.False(remaining.Value<bool>("flush"));
        Assert.Equal(
            21_800,
            remaining["points"]!.First()!.Value<int>("t"));

        remainingUploadRelease.SetResult();
        await device.Stop();
    }

    [Fact]
    public async Task LoopSeekStreamsEveryPointThroughEndAndBeforeSeek()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var actions = Enumerable.Range(0, 160)
            .Select(index => new CmdLinear
            {
                AbsoluteTime = index * 100,
                Value = index
            })
            .ToList();
        AddGallery(rig.Funscripts, new FunscriptGallery
        {
            Name = "complete-seeked-loop",
            Variant = "default",
            Duration = Convert.ToInt32(actions[^1].AbsoluteTime),
            Loop = true,
            Commands = actions
        });

        await using var client = new ImmediateSyncClient(
            maxPointsPerRequest: 50,
            bufferCapacity: 60,
            expectedAddCount: 3);
        var now = new DateTimeOffset(
            2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var device = new HandyV3Device(
            client,
            rig.Funscripts,
            NullLogger.Instance,
            (delay, token) =>
            {
                now = now.Add(delay);
                return Task.CompletedTask;
            },
            () => now);
        device.selectedVariant = "default";

        await device.PlayGallery("complete-seeked-loop", seek: 14_000);
        await client.AddRequestsCompleted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        await client.FollowUpSyncSent.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        var play = Assert.Single(client.PlayRequests);
        Assert.NotNull(play.add);
        var chunks = new[] { play.add }
            .Concat(client.AddRequests)
            .ToList();
        Assert.Equal([50, 50, 50, 10], chunks
            .Select(chunk => chunk.points.Count));
        Assert.Equal([50, 100, 150, 160], chunks
            .Select(chunk => chunk.tail_point_stream_index));

        var sentPoints = chunks
            .SelectMany(chunk => chunk.points)
            .ToList();
        var duration = Convert.ToInt32(actions[^1].AbsoluteTime);
        var expectedPoints = actions
            .Skip(139)
            .Select(action => new Point(
                Convert.ToInt32(action.AbsoluteTime),
                Math.Clamp(Convert.ToInt32(action.Value), 0, 100)))
            .Concat(actions
                .Take(139)
                .Select(action => new Point(
                    Convert.ToInt32(action.AbsoluteTime) + duration,
                    Math.Clamp(Convert.ToInt32(action.Value), 0, 100))))
            .ToList();
        Assert.Equal(expectedPoints, sentPoints);
        Assert.Contains(sentPoints, point => point.t > duration);
        Assert.True(client.SyncRequests.Last().current_time > duration);
        Assert.False(device.SelfManagedLoop);

        await device.Stop();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SmallScriptEmbedsFlushAndPointsInPlay(bool loop)
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var repository = rig.Funscripts;
        AddGallery(repository, new FunscriptGallery
        {
            Name = "small-loop",
            Variant = "default",
            Duration = 1000,
            Loop = loop,
            Commands =
            [
                new CmdLinear { AbsoluteTime = 0, Value = 0 },
                new CmdLinear { AbsoluteTime = 1000, Value = 100 }
            ]
        });

        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(RecordingHttpMessageHandler.JsonResponse(
                HspStateJson(points: 2, maxPoints: 200, tail: 2))));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add("X-Connection-Key", "TEST-KEY");

        var device = new HandyV3Device(
            new HandyHttpClient(client),
            repository,
            NullLogger.Instance);
        device.selectedVariant = "default";

        await device.PlayGallery("small-loop");

        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path == "v3/hsp/add");
        var play = JObject.Parse(handler.Requests.Single(
            request => request.Path == "v3/hsp/play").Content!);
        Assert.Equal(loop, play.Value<bool>("loop"));
        Assert.True(play["add"]!.Value<bool>("flush"));
        Assert.Equal(2, play["add"]!["points"]!.Count());

        await device.Stop();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongScriptStreamsPointsAfterPlaybackStarts(bool loop)
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var repository = rig.Funscripts;
        AddGallery(repository, new FunscriptGallery
        {
            Name = "long-scene",
            Variant = "default",
            Duration = 10000,
            Loop = loop,
            Commands = Enumerable.Range(0, 6)
                .Select(index => new CmdLinear
                {
                    AbsoluteTime = index,
                    Value = index * 10
                })
                .ToList()
        });

        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(RecordingHttpMessageHandler.JsonResponse(
                HspStateJson(points: 3, maxPoints: 3, tail: 3))));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add("X-Connection-Key", "TEST-KEY");

        var device = new HandyV3Device(
            new HandyHttpClient(client),
            repository,
            NullLogger.Instance);
        device.selectedVariant = "default";

        await device.PlayGallery("long-scene");

        var playRequest = handler.Requests.Single(
            request => request.Path == "v3/hsp/play");
        var play = JObject.Parse(playRequest.Content!);
        Assert.False(play.Value<bool>("loop"));
        Assert.False(device.SelfManagedLoop);
        Assert.True(play["add"]!.Value<bool>("flush"));
        Assert.Equal(3, play["add"]!["points"]!.Count());

        await handler.WaitForPathAsync("v3/hsp/add");
        var streamedAdd = JObject.Parse(handler.Requests.Single(
            request => request.Path == "v3/hsp/add").Content!);
        Assert.False(streamedAdd.Value<bool>("flush"));
        Assert.Equal(3, streamedAdd["points"]!.Count());

        await device.Stop();
    }

    [Fact]
    public async Task RemainingPointsUploadFourSecondsBeforeBufferSpaceIsNeeded()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var repository = rig.Funscripts;
        AddGallery(repository, new FunscriptGallery
        {
            Name = "streaming-grace",
            Variant = "default",
            Duration = 12_500,
            Loop = false,
            Commands = Enumerable.Range(0, 6)
                .Select(index => new CmdLinear
                {
                    AbsoluteTime = index * 2_500,
                    Value = index * 10
                })
                .ToList()
        });

        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(RecordingHttpMessageHandler.JsonResponse(
                HspStateJson(points: 3, maxPoints: 3, tail: 3))));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add("X-Connection-Key", "TEST-KEY");

        var streamingDelayStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStreamingDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var now = new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);
        var device = new TestableHandyV3Device(
            new HandyHttpClient(client),
            repository,
            async (delay, token) =>
            {
                if (delay == TimeSpan.FromMilliseconds(250))
                {
                    streamingDelayStarted.SetResult();
                    await releaseStreamingDelay.Task.WaitAsync(token);
                    return;
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
            () => now);
        device.selectedVariant = "default";

        await device.PlayGallery("streaming-grace");
        await streamingDelayStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path == "v3/hsp/add");

        now = now.AddMilliseconds(1_001);
        releaseStreamingDelay.SetResult();
        await handler.WaitForPathAsync("v3/hsp/add");

        Assert.InRange(device.CurrentTime, 1_001, 1_100);
        await device.Stop();
    }

    [Fact]
    public async Task ConsecutivePlaysReuseStreamAndAdvanceTailIndex()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var repository = rig.Funscripts;
        AddGallery(repository, new FunscriptGallery
        {
            Name = "first",
            Variant = "default",
            Duration = 1000,
            Loop = false,
            Commands =
            [
                new CmdLinear { AbsoluteTime = 0, Value = 10 },
                new CmdLinear { AbsoluteTime = 1000, Value = 90 }
            ]
        });
        AddGallery(repository, new FunscriptGallery
        {
            Name = "second",
            Variant = "default",
            Duration = 1000,
            Loop = true,
            Commands =
            [
                new CmdLinear { AbsoluteTime = 0, Value = 30 },
                new CmdLinear { AbsoluteTime = 1000, Value = 70 }
            ]
        });

        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            var isSetup =
                request.RequestUri?.AbsolutePath.EndsWith("/hsp/setup") == true;
            return Task.FromResult(RecordingHttpMessageHandler.JsonResponse(
                isSetup
                    ? HspStateJson(points: 0, maxPoints: 200, tail: 0)
                    : HspStateJson(points: 2, maxPoints: 200, tail: 2)));
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add("X-Connection-Key", "TEST-KEY");

        var device = new HandyV3Device(
            new HandyHttpClient(client),
            repository,
            NullLogger.Instance);
        device.selectedVariant = "default";

        await device.PlayGallery("first");
        await device.PlayGallery("second");

        Assert.Single(
            handler.Requests,
            request => request.Path == "v3/hsp/setup");
        var plays = handler.Requests
            .Where(request => request.Path == "v3/hsp/play")
            .Select(request => JObject.Parse(request.Content!))
            .ToList();
        Assert.Equal(2, plays.Count);
        Assert.True(plays.All(play =>
            play["add"]!.Value<bool>("flush")));
        Assert.Equal(
            [2, 4],
            plays.Select(play =>
                play["add"]!.Value<int>("tail_point_stream_index")));
        Assert.Equal(10, plays[0]["add"]!["points"]![0]!.Value<int>("x"));
        Assert.Equal(30, plays[1]["add"]!["points"]![0]!.Value<int>("x"));

        await device.Stop();
    }

    [Fact]
    public async Task ConcurrentFirstPlaysShareSingleSessionSetup()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var repository = rig.Funscripts;
        AddGallery(repository, new FunscriptGallery
        {
            Name = "first",
            Variant = "default",
            Duration = 1000,
            Loop = false,
            Commands =
            [
                new CmdLinear { AbsoluteTime = 0, Value = 10 },
                new CmdLinear { AbsoluteTime = 1000, Value = 90 }
            ]
        });
        AddGallery(repository, new FunscriptGallery
        {
            Name = "second",
            Variant = "default",
            Duration = 1000,
            Loop = false,
            Commands =
            [
                new CmdLinear { AbsoluteTime = 0, Value = 30 },
                new CmdLinear { AbsoluteTime = 1000, Value = 70 }
            ]
        });

        var setupRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHttpMessageHandler(
            async (request, token) =>
            {
                var isSetup =
                    request.RequestUri?.AbsolutePath.EndsWith(
                        "/hsp/setup") == true;
                if (isSetup)
                    await setupRelease.Task.WaitAsync(token);

                return RecordingHttpMessageHandler.JsonResponse(
                    isSetup
                        ? HspStateJson(points: 0, maxPoints: 200, tail: 0)
                        : HspStateJson(points: 2, maxPoints: 200, tail: 2));
            });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add("X-Connection-Key", "TEST-KEY");

        var device = new HandyV3Device(
            new HandyHttpClient(client),
            repository,
            NullLogger.Instance);
        device.selectedVariant = "default";

        var firstPlay = device.PlayGallery("first");
        await handler.WaitForPathAsync("v3/hsp/setup");
        var secondPlay = device.PlayGallery("second");

        Assert.Single(
            handler.Requests,
            request => request.Path == "v3/hsp/setup");

        setupRelease.SetResult();
        await Task.WhenAll(firstPlay, secondPlay);

        Assert.Single(
            handler.Requests,
            request => request.Path == "v3/hsp/setup");
        var play = JObject.Parse(handler.Requests.Single(
            request => request.Path == "v3/hsp/play").Content!);
        Assert.Equal(30, play["add"]!["points"]![0]!.Value<int>("x"));

        await device.Stop();
    }

    [Fact]
    public async Task ShortDelayedSynchronizationCompletesBeforeStreamingMorePoints()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        AddGallery(rig.Funscripts, new FunscriptGallery
        {
            Name = "immediate-sync",
            Variant = "default",
            Duration = 10_000,
            Loop = true,
            Commands =
            [
                new CmdLinear { AbsoluteTime = 0, Value = 10 },
                new CmdLinear { AbsoluteTime = 1, Value = 30 },
                new CmdLinear { AbsoluteTime = 2, Value = 60 },
                new CmdLinear { AbsoluteTime = 3, Value = 90 }
            ]
        });

        await using var client = new ImmediateSyncClient();
        var observedDelays = new ConcurrentQueue<TimeSpan>();
        var followUpDelayStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFollowUpDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var device = new HandyV3Device(
            client,
            rig.Funscripts,
            NullLogger.Instance,
            async (delay, token) =>
            {
                observedDelays.Enqueue(delay);
                if (delay == TimeSpan.FromSeconds(3))
                {
                    followUpDelayStarted.TrySetResult();
                    await releaseFollowUpDelay.Task.WaitAsync(token);
                }
            });
        device.selectedVariant = "default";

        await device.PlayGallery("immediate-sync");
        await client.RemainingPointsAdded.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        await followUpDelayStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["setup", "play", "sync", "add"],
            client.Operations);
        releaseFollowUpDelay.SetResult();
        await client.FollowUpSyncSent.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(
            ["setup", "play", "sync", "add", "sync"],
            client.Operations);
        Assert.Equal(
            [TimeSpan.FromMilliseconds(15), TimeSpan.FromSeconds(3)],
            observedDelays);
        await device.Stop();
    }

    [Fact]
    public async Task FirstPlaySynchronizesClocksThenCorrectsTimeAfterWarmup()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var repository = rig.Funscripts;
        AddGallery(repository, new FunscriptGallery
        {
            Name = "warmup",
            Variant = "default",
            Duration = 10_000,
            Loop = false,
            Commands =
            [
                new CmdLinear { AbsoluteTime = 0, Value = 10 },
                new CmdLinear { AbsoluteTime = 10_000, Value = 90 }
            ]
        });

        var delayStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClockSynchronization = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var clockUsedAsyncMode = false;
        var handler = new RecordingHttpMessageHandler(
            async (request, token) =>
            {
                if (request.RequestUri?.AbsolutePath.EndsWith(
                        "/hstp/clocksync") == true)
                {
                    clockUsedAsyncMode =
                        request.RequestUri.Query.Contains("s=false");
                    await releaseClockSynchronization.Task.WaitAsync(token);
                }

                return RecordingHttpMessageHandler.JsonResponse(
                    HspStateJson(points: 2, maxPoints: 200, tail: 2));
            });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add("X-Connection-Key", "TEST-KEY");

        var device = new HandyV3Device(
            new HandyHttpClient(client),
            repository,
            NullLogger.Instance,
            async (delay, token) =>
            {
                if (delay == TimeSpan.FromSeconds(3))
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return;
                }

                Assert.Equal(TimeSpan.FromMilliseconds(1500), delay);
                delayStarted.SetResult();
                await releaseDelay.Task.WaitAsync(token);
            });
        device.selectedVariant = "default";

        await device.PlayGallery("warmup");
        await delayStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "v3/hstp/clocksync",
                "v3/hsp/setup",
                "v3/hsp/play"
            ],
            handler.Requests.Select(request => request.Path));
        Assert.True(clockUsedAsyncMode);

        releaseDelay.SetResult();
        Assert.DoesNotContain(
            handler.Requests,
            request => request.Path == "v3/hsp/synctime");

        var syncRequest = await handler.WaitForPathAsync(
            "v3/hsp/synctime");
        releaseClockSynchronization.SetResult();
        var sync = JObject.Parse(syncRequest.Content!);
        Assert.InRange(sync.Value<int>("current_time"), 0, 10_000);
        Assert.True(sync.Value<long>("server_time") > 0);
        Assert.Equal(1.0, sync.Value<double>("filter"));

        await device.Stop();
    }

    [Fact]
    public async Task DeviceClockSynchronizationExpiresAfterTwentyMinutes()
    {
        await using var rig = await PlayerTestRig.CreateAsync();
        var repository = rig.Funscripts;
        AddGallery(repository, new FunscriptGallery
        {
            Name = "clock-expiration",
            Variant = "default",
            Duration = 10_000,
            Loop = false,
            Commands =
            [
                new CmdLinear { AbsoluteTime = 0, Value = 10 },
                new CmdLinear { AbsoluteTime = 10_000, Value = 90 }
            ]
        });

        var now = new DateTimeOffset(
            2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(RecordingHttpMessageHandler.JsonResponse(
                HspStateJson(points: 2, maxPoints: 200, tail: 2))));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://handy.test/")
        };
        client.DefaultRequestHeaders.Add("X-Connection-Key", "TEST-KEY");

        var device = new HandyV3Device(
            new HandyHttpClient(client),
            repository,
            NullLogger.Instance,
            (_, token) => Task.Delay(
                Timeout.InfiniteTimeSpan,
                token),
            () => now);
        device.selectedVariant = "default";

        await device.PlayGallery("clock-expiration");
        await device.PlayGallery("clock-expiration");
        Assert.Single(
            handler.Requests,
            request => request.Path == "v3/hstp/clocksync");

        now = now.AddMinutes(21);
        await device.PlayGallery("clock-expiration");
        Assert.Equal(
            2,
            handler.Requests.Count(
                request => request.Path == "v3/hstp/clocksync"));

        await device.Stop();
    }

    private static string HspStateJson(int points, int maxPoints, int tail)
        => $$"""
             {
               "result": {
                 "stream_id": 123,
                 "max_points": {{maxPoints}},
                 "points": {{points}},
                 "current_point": 0,
                 "current_time": 0,
                 "loop": false,
                 "playback_rate": 1.0,
                 "first_point_time": 0,
                 "last_point_time": 0,
                 "play_state": "stopped",
                 "tail_point_stream_index": {{tail}},
                 "tail_point_stream_index_threshold": 0
               }
             }
             """;

    private static void AddGallery(
        FunscriptRepository repository,
        FunscriptGallery gallery)
    {
        var property = typeof(FunscriptRepository).GetProperty(
            "Galleries",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "FunscriptRepository.Galleries was not found.");
        var galleries =
            (Dictionary<string, List<FunscriptGallery>>)property
                .GetValue(repository)!;
        galleries[gallery.Name] = [gallery];
    }

    private sealed class TestableHandyV3Device : HandyV3Device
    {
        private readonly Func<DateTime> _getUtcNow;

        public TestableHandyV3Device(
            IHandyClient client,
            FunscriptRepository repository,
            Func<TimeSpan, CancellationToken, Task> delay,
            Func<DateTime> getUtcNow)
            : base(
                client,
                repository,
                NullLogger.Instance,
                delay,
                () => new DateTimeOffset(getUtcNow()))
        {
            _getUtcNow = getUtcNow;
        }

        internal override DateTime GetUtcNow() => _getUtcNow();
    }

    private sealed class ImmediateSyncClient : IHandyClient
    {
        private static readonly HspState State = new(
            stream_id: 1,
            max_points: 2,
            points: 2,
            current_point: 0,
            current_time: 0,
            loop: true,
            playback_rate: 1,
            first_point_time: 0,
            last_point_time: 3,
            play_state: "playing",
            tail_point_stream_index: 2,
            tail_point_stream_index_threshold: 0);

        private readonly ConcurrentQueue<string> operations = new();
        private readonly HspState state;
        private readonly int maxPointsPerRequest;
        private readonly int expectedAddCount;
        private int addCount;

        public ImmediateSyncClient(
            int maxPointsPerRequest = 2,
            int bufferCapacity = 2,
            int expectedAddCount = 1)
        {
            this.maxPointsPerRequest = maxPointsPerRequest;
            this.expectedAddCount = expectedAddCount;
            state = State with
            {
                max_points = bufferCapacity,
                tail_point_stream_index = 0
            };
        }

        public string Id => "test:immediate-sync";
        public string Key => string.Empty;
        public string DisplayName => "Immediate sync test client";
        public int MaxPointsPerRequest => maxPointsPerRequest;
        public TimeSpan PlaybackSyncDelay =>
            TimeSpan.FromMilliseconds(15);
        public IReadOnlyList<string> Operations => operations.ToArray();
        public ConcurrentQueue<HspPlayRequest> PlayRequests { get; } = [];
        public ConcurrentQueue<HspAddRequest> AddRequests { get; } = [];
        public ConcurrentQueue<HspSyncTimeRequest> SyncRequests { get; } = [];
        public TaskCompletionSource RemainingPointsAdded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AddRequestsCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FollowUpSyncSent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int syncCount;

        public event Action<IHandyClient> Disconnected
        {
            add { }
            remove { }
        }

        public Task SynchronizeClock(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<HspState> Setup(
            HspSetupRequest request,
            CancellationToken cancellationToken)
        {
            operations.Enqueue("setup");
            return Task.FromResult(state);
        }

        public Task<HspState> AddPoints(
            HspAddRequest request,
            CancellationToken cancellationToken)
        {
            operations.Enqueue("add");
            AddRequests.Enqueue(request);
            RemainingPointsAdded.TrySetResult();
            if (Interlocked.Increment(ref addCount) == expectedAddCount)
                AddRequestsCompleted.TrySetResult();
            return Task.FromResult(state);
        }

        public Task<HspState> Play(
            HspPlayRequest request,
            CancellationToken cancellationToken)
        {
            operations.Enqueue("play");
            PlayRequests.Enqueue(request);
            return Task.FromResult(state);
        }

        public Task<HspState> SyncTime(
            HspSyncTimeRequest request,
            CancellationToken cancellationToken)
        {
            operations.Enqueue("sync");
            SyncRequests.Enqueue(request);
            if (Interlocked.Increment(ref syncCount) == 2)
                FollowUpSyncSent.TrySetResult();
            return Task.FromResult(state);
        }

        public Task Stop(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetStroke(
            SlideRequest request,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task SetOffset(
            int offset,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
