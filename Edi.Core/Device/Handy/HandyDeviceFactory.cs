using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Edi.Core.Gallery.Index;
using Edi.Core.Gallery.Funscript;

namespace Edi.Core.Device.Handy
{
    /// <summary>
    /// Helper class to detect Handy device version and create the appropriate device instance
    /// </summary>
    public class HandyDeviceFactory
    {
        private readonly ILogger _logger;

        public HandyDeviceFactory(ILogger logger)
        {
            _logger = logger;
        }


        /// <summary>
        /// Detects the firmware version of the Handy device via /info endpoint
        /// </summary>
        /// <param name="client">HttpClient configured for the device</param>
        /// <returns>Firmware version as string (e.g., "3.2.0")</returns>
        public async Task<string> DetectFirmwareVersionAsync(HttpClient client)
        {
            try
            {
                _logger.LogInformation("Detecting Handy device firmware version");

                var response = await client.GetAsync("v2/info");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Failed to get device info. Status: {response.StatusCode}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var infoResponse = JsonConvert.DeserializeObject<HandyInfoResponse>(content);

                if (infoResponse?.FwVersion == null)
                {
                    _logger.LogWarning("Failed to parse firmware version from device info response");
                    return null;
                }

                _logger.LogInformation($"Detected firmware version: {infoResponse.FwVersion}");
                return infoResponse.FwVersion;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error detecting firmware version: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses version string and determines if device should use HSP protocol (v3.0+)
        /// </summary>
        /// <param name="firmwareVersion">Firmware version string (e.g., "3.2.0")</param>
        /// <returns>true if version >= 3.0.0, false otherwise</returns>
        public bool ShouldUseHspProtocol(string firmwareVersion)
        {
            if (string.IsNullOrWhiteSpace(firmwareVersion))
            {
                _logger.LogWarning("Empty firmware version, defaulting to legacy protocol");
                return false;
            }

            try
            {
                var versionParts = firmwareVersion.Split('.');
                if (versionParts.Length < 1 || !int.TryParse(versionParts[0], out var majorVersion))
                {
                    _logger.LogWarning($"Could not parse major version from '{firmwareVersion}', defaulting to legacy protocol");
                    return false;
                }

                bool useHsp = majorVersion >= 4;
                _logger.LogInformation($"Device version {firmwareVersion}: Using {(useHsp ? "HSP (v3+)" : "Legacy HSSP")} protocol");

                return useHsp;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing firmware version '{firmwareVersion}': {ex.Message}, defaulting to legacy protocol");
                return false;
            }
        }
    }
    

    public record HandyInfoResponse(
        [property: JsonProperty("fw_status")] int FwStatus,
        [property: JsonProperty("fw_version")] string FwVersion,
        [property: JsonProperty("fw_feature_flags")] string FwFeatureFlags,
        [property: JsonProperty("hw_model_no")] int HwModelNo,
        [property: JsonProperty("hw_model_name")] string HwModelName,
        [property: JsonProperty("hw_model_variant")] int HwModelVariant
    );
}
