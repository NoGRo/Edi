using Newtonsoft.Json;

namespace Edi.Core.Device.Handy;

public interface IHandyClient : IAsyncDisposable
{
    string Id { get; }
    string Key { get; }
    string DisplayName { get; }
    int MaxPointsPerRequest { get; }
    event Action<IHandyClient> Disconnected;

    Task SynchronizeClock(CancellationToken cancellationToken);

    Task<HspState> Setup(
        HspSetupRequest request,
        CancellationToken cancellationToken);

    Task<HspState> AddPoints(
        HspAddRequest request,
        CancellationToken cancellationToken);

    Task<HspState> Play(
        HspPlayRequest request,
        CancellationToken cancellationToken);

    Task<HspState> SyncTime(
        HspSyncTimeRequest request,
        CancellationToken cancellationToken);

    Task Stop(CancellationToken cancellationToken);

    Task SetStroke(
        SlideRequest request,
        CancellationToken cancellationToken);

    Task SetOffset(int offset, CancellationToken cancellationToken);
}

public record HspSetupRequest(int stream_id);

public record HspState(
    int stream_id,
    int max_points,
    int points,
    int current_point,
    long current_time,
    bool loop,
    double playback_rate,
    long first_point_time,
    long last_point_time,
    string play_state,
    int tail_point_stream_index,
    int tail_point_stream_index_threshold);

public record HspStateResult(HspState result);

public record OffsetRequest(int offset);

public record HspAddRequest(
    List<Point> points,
    bool flush,
    int tail_point_stream_index);

public record HspPlayRequest(
    int start_time,
    long server_time,
    double playback_rate,
    bool loop,
    [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    HspAddRequest add);

public record HspSyncTimeRequest(
    int current_time,
    long server_time,
    double filter);

public record Point(int t, int x);
