using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Edi.Core.Device.Handy.Transport
{
    /// <summary>
    /// Handles high-level Handy operations using an abstracted transport layer.
    /// This decouples the device logic from the specific transport protocol.
    /// </summary>
    public class HandyCommandExecutor
    {
        private readonly IHandyTransport _transport;
        private readonly ILogger _logger;

        public HandyCommandExecutor(IHandyTransport transport, ILogger logger)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Initializes HSP session on the device.
        /// </summary>
        public async Task<HandyHspSetupResult?> SetupHspSessionAsync(int streamId, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { stream_id = streamId });
                var response = await _transport.PutAsync("v3/hsp/setup", payload, cancellationToken);

                if (!response.Success)
                {
                    _logger.LogError($"HSP setup failed: {response.ErrorMessage}");
                    return null;
                }

                var result = JsonConvert.DeserializeObject<HandyHspSetupResult>(response.Content);
                _logger.LogInformation($"HSP setup successful. StreamId: {result?.result?.stream_id}, MaxPoints: {result?.result?.max_points}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error setting up HSP session: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Adds points to the HSP buffer.
        /// </summary>
        public async Task<HandyHspAddResult?> AddPointsAsync(HandyHspAddRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(request);
                var response = await _transport.PutAsync("v3/hsp/add", payload, cancellationToken);

                if (!response.Success)
                {
                    _logger.LogError($"Add points failed: {response.ErrorMessage}");
                    return null;
                }

                var result = JsonConvert.DeserializeObject<HandyHspAddResult>(response.Content);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error adding points: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sends HSP play command.
        /// </summary>
        public async Task<HandyHspPlayResult?> PlayAsync(HandyHspPlayRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(request);
                var response = await _transport.PutAsync("v3/hsp/play", payload, cancellationToken);

                if (!response.Success)
                {
                    _logger.LogError($"Play command failed: {response.ErrorMessage}");
                    return null;
                }

                var result = JsonConvert.DeserializeObject<HandyHspPlayResult>(response.Content);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending play command: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sends HSP stop command.
        /// </summary>
        public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _transport.PutAsync("v3/hsp/stop", "{}", cancellationToken);

                if (!response.Success)
                {
                    _logger.LogError($"Stop command failed: {response.ErrorMessage}");
                    return false;
                }

                _logger.LogInformation("Stop command sent successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending stop command: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets the slide range (position).
        /// </summary>
        public async Task<bool> SetSlideAsync(int min, int max, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { min, max });
                var response = await _transport.PutAsync("v2/slide", payload, cancellationToken);

                if (!response.Success)
                {
                    _logger.LogError($"Set slide failed: {response.ErrorMessage}");
                    return false;
                }

                _logger.LogInformation($"Slide set to {min}-{max}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error setting slide: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets the device mode.
        /// </summary>
        public async Task<bool> SetModeAsync(int mode, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { mode });
                var response = await _transport.PutAsync("v2/mode", payload, cancellationToken);

                if (!response.Success)
                {
                    _logger.LogError($"Set mode failed: {response.ErrorMessage}");
                    return false;
                }

                _logger.LogInformation($"Mode set to {mode}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error setting mode: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets the HSP offset.
        /// </summary>
        public async Task<bool> SetHspOffsetAsync(int offset, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { offset });
                var response = await _transport.PutAsync("v2/hstp/offset", payload, cancellationToken);

                if (!response.Success)
                {
                    _logger.LogError($"Set offset failed: {response.ErrorMessage}");
                    return false;
                }

                _logger.LogInformation($"Offset set to {offset}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error setting offset: {ex.Message}");
                return false;
            }
        }
    }

    #region HSP Models

    public record HandyHspSetupResult(HandyHspState result);

    public record HandyHspState(
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

    public record HandyPoint(int t, int x)
    {
        public HandyPoint() : this(default, default) { }
    }

    public record HandyHspAddRequest(
        System.Collections.Generic.List<HandyPoint> points,
        bool flush,
        int tail_point_stream_index);

    public record HandyHspPlayAddRequest(System.Collections.Generic.IEnumerable<HandyPoint> points);

    public record HandyHspPlayRequest(
        int start_time,
        long server_time,
        double playback_rate,
        bool flush,
        bool loop,
        HandyHspPlayAddRequest add);

    public record HandyHspAddResult(HandyHspState? result);

    public record HandyHspPlayResult(HandyHspState? result);

    #endregion
}
