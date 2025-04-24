using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Edi.Core;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery;
using System.IO;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Edi.Core.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class EdiController(IEdi edi, ConfigurationManager configurationManager) : ControllerBase
    {
        
        [HttpPost("Play/{name}")]
        public async Task Play([FromRoute,Required] string name, [FromQuery] long seek = 0, [FromQuery] string[] channels = null)
        => await edi.Player.Play(name, seek, channels);

        /// <summary>
        /// Stops the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpPost("Stop")]
        public async Task Stop([FromQuery] string[] channels = null)
        => await edi.Player.Stop(channels);

        /// <summary>
        /// Pauses the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpPost("Pause")]
        public async Task Pause([FromQuery] bool untilResume = false, [FromQuery] string[] channels = null)
        => await edi.Player.Pause(untilResume, channels);

        /// <summary>
        /// Resumes the playback of the current gallery of multimedia content.
        /// </summary>
        [HttpPost("Resume")]
        public async Task Resume([FromQuery] bool AtCurrentTime = false, [FromQuery] string[] channels = null)
        => await edi.Player.Resume(AtCurrentTime, channels);

        [HttpPost("Intensity/{max}")]
        public async Task Intensity([Required, FromRoute, Range(0, 100)] int max = 100, [FromQuery] string[] channels = null)
        => await edi.Player.Intensity(max, channels);





        [HttpGet("Definitions")]
        public async Task<IEnumerable<DefinitionGallery>> GetDefinitions()
            => edi.Definitions.ToArray();

        [HttpGet("Assets")]
        public IActionResult Get()
        {

            var galleryPath = configurationManager.Get<GalleryConfig>().GalleryPath.Trim();

            var uploadPath = Path.Combine(Core.Edi.OutputDir, "Upload");

            var allFiles  =  new List<string>();

            if (Directory.Exists(galleryPath))
            {
                allFiles.AddRange(Directory.EnumerateFiles(galleryPath, "*.*", SearchOption.AllDirectories)
                                        .Select(x => x.Replace(galleryPath, "/Edi/Assets/").Replace("\\", "/")));
   
            }
            if (Directory.Exists(uploadPath))
            {
                allFiles.AddRange(Directory.EnumerateFiles(uploadPath, "*.*", SearchOption.AllDirectories)
                                        .Select(x => x.Replace(uploadPath, "/Edi/Upload/").Replace("\\", "/")));

            }

            return Ok(allFiles);

        }

        [HttpPost("Assets")]
        public async Task<ActionResult<IEnumerable<DefinitionGallery>>> CreateAssets([FromForm] List<IFormFile> files)
        {
            if (files == null || files.Count == 0)
            {
                return NotFound();
            }

            var folderPath = Path.Combine(Core.Edi.OutputDir, "Upload");
            var uploadedFiles = new List<string>();
            foreach (var file in files)
            {

                var filePath = Path.Combine(folderPath, file.FileName);

                if (Directory.Exists(filePath))
                    Directory.Delete(filePath, true);

                Directory.CreateDirectory(folderPath);

                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }

                try
                {
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }
                    uploadedFiles.Add(file.FileName);
                }
                catch (Exception ex)
                {
                    // Podrías manejar de manera diferente los errores de cada archivo.
                }
                // Proceso con edi.LoadFile si es necesario
               
            }
            await edi.Init(folderPath);

            return Ok(edi.Definitions);
        }
    
    }
}
