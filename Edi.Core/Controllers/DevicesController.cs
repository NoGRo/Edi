using Edi.Core;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

<<<<<<<< HEAD:Edi.Mvc/Controllers/DevicesController.cs
namespace Edi.Rest.Controllers
========
namespace Edi.Core.Controllers
>>>>>>>> master:Edi.Core/Controllers/DevicesController.cs
{
    [ApiController]
    [Route("[controller]")]
    public class DevicesController : Controller
    {

        private readonly IEdi _edi;

        public DevicesController(IEdi edi)
        {
            _edi = edi;
        }

        [HttpGet()]
        public async Task<IEnumerable<DeviceDto>> GetDevices()
        {
            return _edi.Devices.Select(x => new DeviceDto
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
            var device = _edi.Devices.FirstOrDefault(x => x.Name == deviceName);

            if (device == null)
                return NotFound("Device not found");

            if (!device.Variants.Contains(variantName))
                return NotFound("Variant not found");

            await _edi.DeviceManager.SelectVariant(device, variantName);
            return Ok();
        }

        [HttpPost("{deviceName}/Range/{min}-{max}")]
        public async Task<IActionResult> SelectRange([FromRoute, Required] string deviceName,
                                                     [FromRoute, Range(0, 100)] int min,
                                                     [FromRoute, Range(0, 100)] int max)
        {
            var device = _edi.Devices.  FirstOrDefault(x => x.Name == deviceName);

            if (device == null)
                return NotFound("Device not found");
            if (max < min)
                return BadRequest("Max must be greater than Min");

            await _edi.DeviceManager.SelectRange(device, min, max);
            return Ok();
        }
    }
}
