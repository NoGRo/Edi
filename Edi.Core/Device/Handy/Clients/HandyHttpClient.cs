using Newtonsoft.Json;
using System.Text;

namespace Edi.Core.Device.Handy;

internal sealed class HandyHttpClient(HttpClient client) : IHandyClient
{
    public string Id => $"internet:{Key}";

    public string Key { get; } = client.DefaultRequestHeaders
        .GetValues("X-Connection-Key")
        .First();

    public string DisplayName => $"The Handy [{Key}]";

    public int MaxPointsPerRequest => 100;

    public event Action<IHandyClient> Disconnected
    {
        add { }
        remove { }
    }

    public async Task<HspState> Setup(
        HspSetupRequest request,
        CancellationToken cancellationToken)
        => await PutForState(
            "v3/hsp/setup",
            request,
            "setup",
            cancellationToken);

    public async Task<HspState> AddPoints(
        HspAddRequest request,
        CancellationToken cancellationToken)
        => await PutForState(
            "v3/hsp/add",
            request,
            "add",
            cancellationToken);

    public async Task<HspState> Play(
        HspPlayRequest request,
        CancellationToken cancellationToken)
        => await PutForState(
            "v3/hsp/play",
            request,
            "play",
            cancellationToken);

    public async Task Stop(CancellationToken cancellationToken)
    {
        using var response = await client.PutAsync(
            "v3/hsp/stop",
            null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetStroke(
        SlideRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await client.PutAsync(
            "v2/slide",
            JsonContent(request),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetOffset(
        int offset,
        CancellationToken cancellationToken)
    {
        using var response = await client.PutAsync(
            "v3/hstp/offset",
            JsonContent(new OffsetRequest(
                HandyConfig.NormalizeOffset(offset))),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<HspState> PutForState(
        string path,
        object request,
        string operation,
        CancellationToken cancellationToken)
    {
        using var response = await client.PutAsync(
            path,
            JsonContent(request),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);
        return JsonConvert
            .DeserializeObject<HspStateResult>(content)
            ?.result
            ?? throw new InvalidOperationException(
                $"The Handy returned an invalid HSP {operation} response.");
    }

    private static StringContent JsonContent(object value)
        => new(
            JsonConvert.SerializeObject(value),
            Encoding.UTF8,
            "application/json");
}
