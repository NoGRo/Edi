using Edi.Core;
using Edi.Core.Device;
using Edi.Core.Device.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Swashbuckle.AspNetCore.Annotations;

namespace Edi.Core.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DevicesController(IEdi edi) : Controller
    {
        [HttpGet()]
        [SwaggerOperation(Summary = "Gets the list of connected devices and their properties.")]
        public IEnumerable<DeviceDto> GetDevices()
        {
            return edi.Devices.Select(x => new DeviceDto
            {
                IsReady = x.IsReady,
                Name = x.Name,
                Variants = x.Variants.ToArray(),
                Channel = x.Channel,
                SelectedVariant = x.SelectedVariant,
                Min = (x as IRange)?.Min ?? 0,
                Max = (x as IRange)?.Max ?? 100,
                BaseMin = edi.DeviceConfiguration.ConfiguredRange(x)?.Min ?? 0,
                BaseMax = edi.DeviceConfiguration.ConfiguredRange(x)?.Max ?? 100,
                OffsetMS =
                    (x as IDeviceWithOffsetConfiguration)
                    ?.OffsetMilliseconds
            });
        }

        [HttpPost("{deviceName}/Variant/{variantName}")]
        [SwaggerOperation(Summary = "Selects a variant for the specified device.")]
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

        [HttpPost("Variants")]
        [SwaggerOperation(Summary = "Selects variants for multiple devices in parallel.")]
        public async Task<IActionResult> SelectVariants(
            [FromBody, Required] Dictionary<string, string> variants, [FromQuery] bool persist = true)
        {
            if (variants is null || variants.Count == 0)
                return BadRequest("At least one device variant is required");

            var selections = new Dictionary<IDevice, string>();
            foreach (var selection in variants)
            {
                var device = edi.Devices.FirstOrDefault(x => x.Name == selection.Key);
                if (device is null)
                    return NotFound($"Device not found: {selection.Key}");
                if (!device.Variants.Contains(selection.Value))
                    return NotFound($"Variant not found for {selection.Key}: {selection.Value}");
                selections[device] = selection.Value;
            }

            await edi.DeviceConfiguration.SelectVariants(selections, persist);
            return Ok();
        }

        [HttpPost("{deviceName}/Range/{min}-{max}")]
        [SwaggerOperation(Summary = "Sets the range (min and max) for the specified device.")]
        public async Task<IActionResult> SelectRange([FromRoute, Required] string deviceName,
                                                     [FromRoute, Range(0, 100)] int min,
                                                     [FromRoute, Range(0, 100)] int max, [FromQuery] bool persist = true)
        {
            var device = edi.Devices.FirstOrDefault(x => x.Name == deviceName);
            if (device == null)
                return NotFound("Device not found");
            if (max < min)
                return BadRequest("Max must be greater than Min");
            await edi.DeviceConfiguration.SelectRange(device, min, max, persist);
            return Ok();
        }

        [HttpPost("{deviceName}/Channel/{channelName}")]
        [SwaggerOperation(Summary = "Assigns a channel to the specified device.")]
        public async Task<IActionResult> SelectRange([FromRoute, Required] string deviceName, [FromRoute, Required] string channelName)
        {
            var device = edi.Devices.FirstOrDefault(x => x.Name == deviceName);
            if (device == null)
                return NotFound("Device not found");
            await edi.DeviceConfiguration.SelectChannel(device, channelName);
            return Ok();
        }

        [HttpPost("{deviceName}/Offset/{offsetMilliseconds}")]
        [SwaggerOperation(
            Summary = "Sets the per-device playback synchronization offset.")]
        public async Task<IActionResult> SelectOffset(
            [FromRoute, Required] string deviceName,
            [FromRoute, Range(
                DeviceOffset.MinimumMilliseconds,
                DeviceOffset.MaximumMilliseconds)] int offsetMilliseconds)
        {
            var device = edi.Devices.FirstOrDefault(
                x => x.Name == deviceName);
            if (device is null)
                return NotFound("Device not found");
            if (device is not IDeviceWithOffsetConfiguration)
                return BadRequest("Device does not support playback offset");

            await edi.DeviceConfiguration.SelectOffset(
                device,
                offsetMilliseconds);
            return Ok();
        }

    }
}
