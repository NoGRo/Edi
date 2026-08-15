using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.Index;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PropertyChanged;
using System.Net;
using System.Text;

namespace Edi.Core.Device.AutoBlow;

[AddINotifyPropertyChangedInterface]
internal class AutoBlowDevice
    : DeviceBase<IndexRepository, IndexGallery>,
      IDeviceWithOffsetConfiguration
{
    private const int UploadAttempts = 3;
    private readonly ILogger _logger;
    private readonly string _deviceType;
    private CancellationTokenSource _uploadCancellation;
    private string _currentBundle = "default";

    public AutoBlowDevice(
        HttpClient client,
        IndexRepository repository,
        ILogger logger,
        int defaultOffset = -80)
        : this(
            client,
            repository,
            logger,
            "AutoBlow",
            defaultOffset)
    {
    }

    protected AutoBlowDevice(
        HttpClient client,
        IndexRepository repository,
        ILogger logger,
        string displayName,
        int defaultOffset = -80)
        : base(repository, logger)
    {
        Client = client;
        _logger = logger;
        _deviceType = displayName;
        EnableOffset(defaultOffset);
        var key = client.DefaultRequestHeaders
            .GetValues("x-device-token")
            .First();
        Name = $"{displayName} [{key}]";
        IsReady = repository.BundlerConfig.DisableBundler;
    }

    public HttpClient Client { get; }

    public void ApplyConfiguration(DeviceConfig configuration)
        => ApplyOffsetConfiguration(configuration);

    internal override void SetVariant()
    {
        if (!repository.BundlerConfig.DisableBundler)
            QueueUpload();
    }

    public override Task PlayGallery(IndexGallery gallery, long seek = 0)
        => PlayGallery(gallery, seek, playCancelTokenSource.Token);

    protected override async Task PlayGallery(
        IndexGallery gallery,
        long seek,
        CancellationToken cancellationToken)
    {
        if (!IsReady && Volatile.Read(ref _uploadCancellation) == null)
        {
            QueueUpload(gallery.Bundle, delay: false);
            return;
        }

        if (gallery.Bundle != _currentBundle)
        {
            gallery = repository.Get(
                gallery.Name,
                SelectedVariant,
                _currentBundle);
            if (gallery.Bundle != _currentBundle)
                QueueUpload(gallery.Bundle, delay: false);
        }

        if (!IsReady)
            return;

        using var response = await Client.PutAsync(
            "sync-script/start",
            new StringContent(
                JsonConvert.SerializeObject(
                    new SyncPlayRequest(gallery.StartTime + seek)),
                Encoding.UTF8,
                "application/json"),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public override async Task StopGallery()
    {
        if (!IsReady)
            return;

        using var response = await Client.PutAsync(
            "sync-script/stop",
            null);
        response.EnsureSuccessStatusCode();
    }

    protected override Task ApplyOffset(
        int offsetMilliseconds,
        CancellationToken cancellationToken)
        => ApplyOffset(
            Client,
            offsetMilliseconds,
            cancellationToken);

    internal static async Task ApplyOffset(
        HttpClient client,
        int offset,
        CancellationToken cancellationToken = default)
    {
        using var response = await client.PutAsync(
            "sync-script/offset",
            new StringContent(
                JsonConvert.SerializeObject(
                    new AutoBlowOffsetRequest(
                        DeviceOffset.Normalize(offset))),
                Encoding.UTF8,
                "application/json"),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private void QueueUpload(string bundle = null, bool delay = true)
    {
        var source = new CancellationTokenSource();
        Interlocked.Exchange(ref _uploadCancellation, source)?.Cancel();
        _ = Upload(bundle, delay, source);
    }

    private async Task Upload(
        string bundle,
        bool delay,
        CancellationTokenSource source)
    {
        try
        {
            if (delay)
                await Task.Delay(3000, source.Token);

            IsReady = false;
            var targetBundle = bundle ?? _currentBundle;
            var targetVariant = selectedVariant;
            var file = repository.GetBundle(
                $"{targetBundle}.{targetVariant}",
                "csv");

            var uploaded = await UploadBundleWithRetry(
                Client,
                file.OpenRead,
                ($"EdiCurrentBundle{targetVariant}" +
                 $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.csv")
                    .ToLowerInvariant(),
                source.Token);
            if (!uploaded)
            {
                _logger.LogWarning(
                    "Bundle upload failed for {DeviceType} after {Attempts} attempts.",
                    _deviceType,
                    UploadAttempts);
                return;
            }

            _currentBundle = targetBundle;
            try
            {
                await ApplyOffset(
                    Client,
                    OffsetMilliseconds,
                    source.Token);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not reapply the offset to {DeviceType}.",
                    _deviceType);
            }

            IsReady = true;
        }
        catch (OperationCanceledException)
            when (source.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error uploading a bundle to {DeviceType}.",
                _deviceType);
        }
        finally
        {
            Interlocked.CompareExchange(
                ref _uploadCancellation,
                null,
                source);
            source.Dispose();
        }
    }

    internal static async Task<bool> UploadBundleWithRetry(
        HttpClient client,
        Func<Stream> openBundle,
        string fileName,
        CancellationToken cancellationToken = default,
        int maximumAttempts = UploadAttempts,
        Func<int, CancellationToken, Task> retryDelay = null)
    {
        retryDelay ??= (attempt, token) =>
            Task.Delay(250 * attempt, token);

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            var retry = false;
            try
            {
                using var bundle = openBundle();
                using var form = new MultipartFormDataContent
                {
                    { new StreamContent(bundle), "file", fileName }
                };
                using var response = await client.PutAsync(
                    "sync-script/upload-csv",
                    form,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                    return true;

                retry = response.StatusCode is
                    HttpStatusCode.RequestTimeout or
                    HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500;
            }
            catch (HttpRequestException)
            {
                retry = true;
            }

            if (!retry || attempt == maximumAttempts)
                return false;

            await retryDelay(attempt, cancellationToken);
        }

        return false;
    }
}

internal sealed record SyncPlayRequest(long startTimeMs);
internal sealed record AutoBlowOffsetRequest(int offsetTimeMs);
internal sealed record ConnectedResponse(bool connected, string cluster);
