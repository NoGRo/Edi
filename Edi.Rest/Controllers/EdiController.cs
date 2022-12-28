using Microsoft.AspNetCore.Mvc;
using Edi.Core.Services;
using Edi.Core.Device.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Edi.Web.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class EdiController : ControllerBase
    {
        private readonly IEdi _edi;

        public EdiController(IEdi edi)
        {
            _edi = edi;
        }

        [HttpPost("Gallery/{name}")]
        [SwaggerOperation(
            Summary = "Plays a gallery.",
            Description = "This method plays a gallery of multimedia content. The `name` parameter specifies the name of the gallery. The `play` parameter is a flag indicating whether to start playback of the gallery. The default value is `true`. The `seek` parameter is the seek position, in milliseconds, from the beginning of the gallery. The default value is `0`."
        )]
        public async Task Gallery([FromRoute]string name, [FromQuery] long seek = 0)
        {
            await _edi.Gallery(name,  seek);
        }

        /// <summary>
        /// Stops the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpPost("StopGallery")]
        public async Task StopGallery()
        {
            await _edi.StopGallery();
        }

        /// <summary>
        /// Pauses the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpPost("Pause")]
        public async Task Pause()
        {
            await _edi.Pause();
        }

        /// <summary>
        /// Resumes the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpPost("Resume")]
        public async Task Resume()
        {
            await _edi.Resume();
        }
    }
}
