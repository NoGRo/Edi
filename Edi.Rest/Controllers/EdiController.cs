using Edi.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Edi.Rest.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class EdiController : ControllerBase
    {
        private readonly ILogger<EdiController> _logger;
        private readonly iEdi edi;

        public EdiController(ILogger<EdiController> logger, iEdi Edi)
        {
            _logger = logger;
            edi = Edi;
        }

        [HttpPost("Play/{Name}")]
        public async Task Play([FromRoute]string Name,[FromQuery]long Seek = 0)
        {
            await edi.Play(Name, Seek);
        }
        [HttpPost("Stop")]
        public async Task Stop()
        {
            await edi.Stop();
        }
        [HttpPost("Filler/{Name}")]
        public async Task Filler([FromRoute] string Name, [FromQuery]bool Play = false,[FromQuery] long Seek = 0)
        {
            await edi.Filler(Name, Play, Seek);
        }
        [HttpPost("Pause")]
        public async Task Pause()
        {
            await edi.Pause();
        }
        [HttpPost("Resume")]
        public async Task Resume()
        {
            await edi.Resume();
        }

    }
}