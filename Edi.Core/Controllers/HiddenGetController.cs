using Edi.Core;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery;
using Edi.Core.Gallery.Definition;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Edi.Core.Controllers
{
    [ApiController]
    [Route("Edi")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class HiddenEdiGetController : ControllerBase
    {
        private readonly IEdi _edi;
        private string[] GetChannels()
        {
            // Primero intenta obtener channels de la query string
            if (Request.Query.TryGetValue("channels", out var queryChannels) && !string.IsNullOrWhiteSpace(queryChannels))
            {
                return queryChannels.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            // Si no hay en query, busca en el header
            var header = Request.Headers["channels"].ToString();
            if (!string.IsNullOrWhiteSpace(header))
            {
                return header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            return null;
        }

        public HiddenEdiGetController(IEdi edi)
        {
            _edi = edi;
        }

        [HttpGet("Play/{name}")]
        public async Task Play([FromRoute] string name, [FromQuery] long seek = 0)
        {
            await _edi.Player.Play(name, seek, GetChannels());
        }

        /// <summary>
        /// Stops the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpGet("Stop")]
        public async Task Stop()
        {
            await _edi.Player.Stop(GetChannels());
        }

        /// <summary>
        /// Pauses the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpGet("Pause")]
        public async Task Pause(bool untilResume = false)
        {
            await _edi.Player.Pause(untilResume, GetChannels());
        }

        /// <summary>
        /// Resumes the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpGet("Resume")]
        public async Task Resume([FromQuery] bool AtCurrentTime = false)
        {
            await _edi.Player.Resume(AtCurrentTime, GetChannels());
        }

        /// <summary>
        /// Set general max Intensity for all devices. 
        /// </summary>
        [HttpGet("Intensity/{max}")]
        public async Task Intensity([Required, FromRoute, Range(0, 100)] int max = 100)
        {
            await _edi.Player.Intensity(max, GetChannels());
        }


    }


    [ApiController]
    [Route("Devices")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class HiddenDevicesGetController : ControllerBase
    {
        private readonly IEdi _edi;

        public HiddenDevicesGetController(IEdi edi)
        {
            _edi = edi;
        }

        [HttpGet("{deviceName}/Variant/{variantName}")]
        public async Task<IActionResult> SelectVarian([FromRoute, Required] string deviceName,
                                                   [FromRoute, Required] string variantName)
        {
            var device = _edi.Devices.FirstOrDefault(x => x.Name == deviceName);

            if (device == null)
                return NotFound("Device not found");

            if (!device.Variants.Contains(variantName))
                return NotFound("Variant not found");

            await _edi.DeviceConfiguration.SelectVariant(device, variantName);
            return Ok();
        }

        [HttpGet("{deviceName}/Range/{min}-{max}")]
        public async Task<IActionResult> SelectRange([FromRoute, Required] string deviceName,
                                                     [FromRoute, Range(0, 100)] int min,
                                                     [FromRoute, Range(0, 100)] int max)
        {
            var device = _edi.Devices.FirstOrDefault(x => x.Name == deviceName);

            if (device == null)
                return NotFound("Device not found");
            if (max < min)
                return BadRequest("Max must be greater than Min");

            await _edi.DeviceConfiguration.SelectRange(device, min, max);
            return Ok();
        }
        [HttpGet("{deviceName}/Channel/{channelName}")]
        public async Task<IActionResult> SelectRange([FromRoute, Required] string deviceName, [FromRoute, Required] string channelName)
        {
            var device = _edi.Devices.FirstOrDefault(x => x.Name == deviceName);
            if (device == null)
                return NotFound("Device not found");
            await _edi.DeviceConfiguration.SelectChannel(device, channelName);
            return Ok();
        }
    }
}