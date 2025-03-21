using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Edi.Core;
using Edi.Core.Device.Interfaces;
using Edi.Forms;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Edi.Controllers
{
    [ApiController]
    [Route("Edi")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class HiddenEdiGetController : ControllerBase
    {
        private readonly IEdi _edi = App.Edi;


        [HttpGet("Play/{name}")]
        public async Task Play([FromRoute] string name, [FromQuery] long seek = 0)
        {
            await _edi.Play(name, seek);
        }

        /// <summary>
        /// Stops the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpGet("Stop")]
        public async Task Stop()
        {
            await _edi.Stop();
        }

        /// <summary>
        /// Pauses the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpGet("Pause")]
        public async Task Pause()
        {
            await _edi.Pause();
        }

        /// <summary>
        /// Resumes the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpGet("Resume")]
        public async Task Resume([FromQuery] bool AtCurrentTime = false)
        {
            await _edi.Resume(AtCurrentTime);
        }

        /// <summary>
        /// Set general max Intensity for all devices. 
        /// </summary>
        [HttpGet("Intensity/{max}")]
        public async Task Intensity([Required, FromRoute, Range(0, 100)] int max = 100)
        {
            await _edi.Intensity(max);
        }


    }


    [ApiController]
    [Route("Devices")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class HiddenDevicesGetController : ControllerBase
    {

        private readonly IEdi _edi = App.Edi;

        [HttpGet("{deviceName}/Variant/{variantName}")]
        public async Task<IActionResult> SelectVarian([FromRoute, Required] string deviceName,
                                                   [FromRoute, Required] string variantName)
        {
            var device = _edi.Devices.FirstOrDefault(x => x.Name == deviceName);

            if (device == null)
                return NotFound("Device not found");

            if (!device.Variants.Contains(variantName))
                return NotFound("Variant not found");

            await _edi.DeviceManager.SelectVariant(device, variantName);
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

            await _edi.DeviceManager.SelectRange(device, min, max);
            return Ok();
        }
    }
}