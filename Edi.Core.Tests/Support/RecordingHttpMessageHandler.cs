using System.Collections.Concurrent;
using System.Net;

namespace Edi.Core.Tests.Support;

internal sealed record RecordedHttpRequest(
    HttpMethod Method,
    string Path,
    string? Content);

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder;
    private readonly ConcurrentQueue<RecordedHttpRequest> requests = new();
    private readonly SemaphoreSlim requestChanged = new(0);

    public RecordingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? responder = null)
    {
        this.responder = responder ?? ((_, _) => Task.FromResult(JsonResponse("{}")));
    }

    public IReadOnlyList<RecordedHttpRequest> Requests => requests.ToList();

    public static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    public async Task<RecordedHttpRequest> WaitForPathAsync(
        string path,
        int occurrence = 1,
        TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));

        while (true)
        {
            var matches = requests.Where(request => request.Path == path).ToList();
            if (matches.Count >= occurrence)
                return matches[occurrence - 1];

            await requestChanged.WaitAsync(cancellation.Token);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var content = request.Content == null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        requests.Enqueue(new RecordedHttpRequest(
            request.Method,
            request.RequestUri?.AbsolutePath.TrimStart('/') ?? string.Empty,
            content));
        requestChanged.Release();

        return await responder(request, cancellationToken);
    }
}
