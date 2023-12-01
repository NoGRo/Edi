using Microsoft.AspNetCore.Mvc;
using Edi.Core;
using Edi.Core.Device.Interfaces;
using Edi.Forms;
using Edi.Core.Gallery.Definition;

namespace Edi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class EdiController : ControllerBase
    {
        private readonly IEdi _edi = App.Edi;

        public EdiController()
        {
        
        }

        [HttpPost("Play/{name}")]
        public async Task Play([FromRoute] string name, [FromQuery] long seek = 0)
        {
            await _edi.Play(name, seek);
        }

        /// <summary>
        /// Stops the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpPost("Stop")]
        public async Task Stop()
        {
            await _edi.Stop();
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

        [HttpGet("Definitions")]
        public async Task<IEnumerable<DefinitionGallery>> GetDefinitions()
            => _edi.Definitions.ToArray();
    }
}
