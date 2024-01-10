using Microsoft.AspNetCore.Mvc;
using Edi.Core;
using Edi.Core.Device.Interfaces;
using Edi.Forms;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery;
using System.IO;

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
        public async Task Resume([FromQuery]bool AtCurrentTime = false)
        {
            await _edi.Resume(AtCurrentTime);
        }

        [HttpGet("Definitions")]
        public async Task<IEnumerable<DefinitionGallery>> GetDefinitions()
            => _edi.Definitions.ToArray();

        [HttpGet("Assets/{*path}")]
        public IActionResult Get(string path)
        {
            // Aquí debes poner la ruta base de tu directorio
            var basePath = _edi.ConfigurationManager.Get<GalleryConfig>().GalleryPath.Trim();

            var fullPath = Path.Combine(basePath, path);

            // Verificar si la ruta es un directorio.
            if (Directory.Exists(fullPath))
            {
                var allFiles = Directory.EnumerateFiles(fullPath, "*.*", SearchOption.AllDirectories)
                                        .Select(x => x.Replace(basePath, ""));
                return Ok(allFiles);
            }

            // Si es un archivo, puedes devolver el archivo o su contenido
            if (System.IO.File.Exists(fullPath))
            {
                var fileContent = System.IO.File.ReadAllBytes(fullPath);
                return File(fileContent, "application/octet-stream", Path.GetFileName(fullPath));
            }

            return NotFound();
        }
    }
}
