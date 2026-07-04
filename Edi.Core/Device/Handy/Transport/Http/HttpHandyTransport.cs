using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Edi.Core.Device.Handy.Transport.Http
{
    /// <summary>
    /// HTTP-based transport implementation for Handy API communication.
    /// This is the default transport that communicates with the Handy API over HTTPS.
    /// </summary>
    public class HttpHandyTransport : IHandyTransport
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private string _firmwareVersion;

        public bool IsConnected { get; private set; }
        public string FirmwareVersion => _firmwareVersion;

        public HttpHandyTransport(HttpClient httpClient, ILogger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("HttpHandyTransport: Attempting connection via HTTP");

                var response = await _httpClient.GetAsync("v2/connected", cancellationToken);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    var status = JsonConvert.DeserializeObject<dynamic>(content);
                    
                    if (status?.connected == true)
                    {
                        IsConnected = true;
                        _logger.LogInformation("HttpHandyTransport: Connected successfully");
                        return true;
                    }
                }

                _logger.LogWarning("HttpHandyTransport: Connection check failed");
                IsConnected = false;
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"HttpHandyTransport: Connection error - {ex.Message}");
                IsConnected = false;
                return false;
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("HttpHandyTransport: Disconnecting");
                IsConnected = false;
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError($"HttpHandyTransport: Disconnect error - {ex.Message}");
            }
        }

        public async Task<HandyResponse> PutAsync(string endpoint, string payload, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug($"HttpHandyTransport: PUT {endpoint}");

                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync(endpoint, content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    return new HandyResponse(true, responseContent);
                }

                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var errorMessage = $"HTTP {(int)response.StatusCode}: {errorContent}";
                _logger.LogWarning($"HttpHandyTransport: PUT failed - {errorMessage}");
                return new HandyResponse(false, null, errorMessage);
            }
            catch (TaskCanceledException ex)
            {
                var errorMessage = $"Request timeout: {ex.Message}";
                _logger.LogWarning($"HttpHandyTransport: {errorMessage}");
                return new HandyResponse(false, null, errorMessage);
            }
            catch (Exception ex)
            {
                var errorMessage = $"PUT request failed: {ex.Message}";
                _logger.LogError($"HttpHandyTransport: {errorMessage}");
                return new HandyResponse(false, null, errorMessage);
            }
        }

        public async Task<HandyResponse> GetAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug($"HttpHandyTransport: GET {endpoint}");

                var response = await _httpClient.GetAsync(endpoint, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    return new HandyResponse(true, responseContent);
                }

                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var errorMessage = $"HTTP {(int)response.StatusCode}: {errorContent}";
                _logger.LogWarning($"HttpHandyTransport: GET failed - {errorMessage}");
                return new HandyResponse(false, null, errorMessage);
            }
            catch (TaskCanceledException ex)
            {
                var errorMessage = $"Request timeout: {ex.Message}";
                _logger.LogWarning($"HttpHandyTransport: {errorMessage}");
                return new HandyResponse(false, null, errorMessage);
            }
            catch (Exception ex)
            {
                var errorMessage = $"GET request failed: {ex.Message}";
                _logger.LogError($"HttpHandyTransport: {errorMessage}");
                return new HandyResponse(false, null, errorMessage);
            }
        }

        public async Task<bool> SetModeAsync(int mode, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { mode });
                var response = await PutAsync("v2/mode", payload, cancellationToken);

                if (!response.Success)
                {
                    _logger.LogError($"SetMode failed: {response.ErrorMessage}");
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

        public async Task<bool> SetHspOffsetAsync(int offset, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = JsonConvert.SerializeObject(new { offset });
                var response = await PutAsync("v2/hstp/offset", payload, cancellationToken);

                if (!response.Success)
                {
                    _logger.LogError($"SetHspOffset failed: {response.ErrorMessage}");
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

        public void SetFirmwareVersion(string version)
        {
            _firmwareVersion = version;
        }
    }
}
