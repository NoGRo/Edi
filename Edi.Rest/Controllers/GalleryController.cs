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

        [HttpGet(Name = "Play/{name}")]
        [HttpPut(Name = "Play/{name}")]
        [HttpPost(Name = "Play/{name}")]
        public async Task Play([FromRoute]string Name,[FromQuery]long Seek = 0)
        {
            edi.Play(Name,Seek);
        }

        public async Task Stop()
        {
            edi.Stop();
        }

    }
}