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

        [HttpPost("SetFiller/{name}")]
        [SwaggerOperation(
            Summary = "Sets the current filler content and, optionally, starts its playback.",
            Description = "This method sets the current filler content and, optionally, starts its playback. The `name` parameter specifies the name of the filler content. The `play` parameter is a flag indicating whether to start playback of the filler content. The default value is `false`. The `seek` parameter is the seek position, in milliseconds, from the beginning of the filler content. The default value is `0`."
        )]
        public async Task SetFiller([FromRoute]string name, [FromQuery] bool play = false, [FromQuery] long seek = 0)
        {
            await _edi.SetFiller(name, play, seek);
        }

        [HttpPost("StopFiller")]
        [SwaggerOperation(
            Summary = "Stops the playback of the current filler content.",
            Description = "This method stops the playback of the current filler content."
        )]
        public async Task StopFiller()
        {
            await _edi.StopFiller();
        }

        [HttpPost("PlayGallery/{name}")]
        [SwaggerOperation(
            Summary = "Plays a gallery.",
            Description = "This method plays a gallery of multimedia content. The `name` parameter specifies the name of the gallery. The `play` parameter is a flag indicating whether to start playback of the gallery. The default value is `true`. The `seek` parameter is the seek position, in milliseconds, from the beginning of the gallery. The default value is `0`."
        )]
        public async Task PlayGallery([FromRoute]string name, [FromQuery] bool play = true, [FromQuery] long seek = 0)
        {
            await _edi.PlayGallery(name, play, seek);
        }

        /// <summary>
        /// Stops the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpPost("StopGallery")]
        public async Task StopGallery()
        {
            await _edi.StopGallery();
        }


        [HttpPost("PlayReaction/{name}")]
        [SwaggerOperation(
          Summary = "Plays a reaction.",
          Description = "This method plays a reaction of multimedia content. The `name` parameter specifies the name of the reaction."
        )]
        public async Task PlayReaction([FromRoute]string name)
        {
            await _edi.PlayReaction(name);
        }

        [HttpPost("StopReaction")]
        [SwaggerOperation(
            Summary = "Stops the playback of the current reaction content.",
            Description = "This method stops the playback of the current reaction content."
        )]
        public async Task StopReaction()
        {
            await _edi.StopReaction();
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
