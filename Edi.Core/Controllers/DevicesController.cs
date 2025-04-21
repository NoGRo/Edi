using Edi.Core;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Edi.Core.Controllers

{
    [ApiController]
    [Route("[controller]")]
    public class DevicesController(IEdi edi) : Controller
    {

        [HttpGet()]
        public async Task<IEnumerable<DeviceDto>> GetDevices()
        {
            return edi.Devices.Select(x => new DeviceDto
            {
                IsReady = x.IsReady,
                Name = x.Name,
                Variants = x.Variants.ToArray(),
                SelectedVariant = x.SelectedVariant,
                Min = (x as IRange)?.Min ?? 0,
                Max = (x as IRange)?.Max ?? 100

            });
        }

        [HttpPost("{deviceName}/Variant/{variantName}")]
        public async Task<IActionResult> SelectVarian([FromRoute, Required] string deviceName,
                                                       [FromRoute, Required] string variantName)
        {
            var device = edi.Devices.FirstOrDefault(x => x.Name == deviceName);

            if (device == null)
                return NotFound("Device not found");

            if (!device.Variants.Contains(variantName))
                return NotFound("Variant not found");

            await edi.DeviceConfiguration.SelectVariant(device, variantName);
            return Ok();
        }

        [HttpPost("{deviceName}/Range/{min}-{max}")]
        public async Task<IActionResult> SelectRange([FromRoute, Required] string deviceName,
                                                     [FromRoute, Range(0, 100)] int min,
                                                     [FromRoute, Range(0, 100)] int max)
        {
            var device = edi.Devices.  FirstOrDefault(x => x.Name == deviceName);

            if (device == null)
                return NotFound("Device not found");
            if (max < min)
                return BadRequest("Max must be greater than Min");

            await edi.DeviceConfiguration.SelectRange(device, min, max);
            return Ok();
        }
    }
}
