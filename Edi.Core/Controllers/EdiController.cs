using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Edi.Core.Device.Interfaces;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery;
using System.IO;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Swashbuckle.AspNetCore.Annotations;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Services;

namespace Edi.Core.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class EdiController(IEdi edi, ConfigurationManager configurationManager) : ControllerBase
    {
        private string[] GetChannels()
        {
            // Primero intenta obtener channels de la query string
            if (Request.Query.TryGetValue("channels", out var queryChannels) && !string.IsNullOrWhiteSpace(queryChannels))
            {
                return queryChannels.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            // Si no hay en query, busca en el header
            var header = Request.Headers["channels"].ToString();
            if (!string.IsNullOrWhiteSpace(header))
            {
                return header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            return null;
        }

        [HttpPost("Play/{name}")]
        [SwaggerOperation(Summary = "Starts playback of a gallery by name.")]
        public async Task Play([FromRoute, Required] string name, [FromQuery] long seek = 0)
        {
            await edi.Player.Play(name, seek, GetChannels());
        }

        [HttpPost("Stop")]
        [SwaggerOperation(Summary = "Stops the playback of the current gallery.")]
        public async Task Stop()
        {
            await edi.Player.Stop(GetChannels());
        }

        [HttpPost("Pause")]
        [SwaggerOperation(Summary = "Pauses the playback of the current gallery.")]
        public async Task Pause(
            [FromQuery, SwaggerParameter("If true, playback will remain paused until an explicit resume command is received. If false, playback resumes automatically on play command.")] bool untilResume = false)
        {
            await edi.Player.Pause(untilResume, GetChannels());
        }

        [HttpPost("Resume")]
        [SwaggerOperation(Summary = "Resumes the playback of the current gallery.")]
        public async Task Resume(
            [FromQuery, SwaggerParameter("If true, playback resumes from the current position. If false, resumes from the last seek position.")] bool AtCurrentTime = false)
        {
            await edi.Player.Resume(AtCurrentTime, GetChannels());
        }

        [HttpPost("Intensity/{max}")]
        [SwaggerOperation(Summary = "Sets the maximum playback intensity for the selected channels.")]
        public async Task Intensity([Required, FromRoute, Range(0, 100)] int max = 100)
        {
            await edi.Player.Intensity(max, GetChannels());
        }

        [HttpGet("Definitions")]
        [SwaggerOperation(Summary = "Gets the list of available gallery definitions.")]
        public IEnumerable<DefinitionResponseDto> GetDefinitions()
            => edi.Definitions.Select(x=> new DefinitionResponseDto(x)).ToArray();


        [HttpGet("Channels")]
        [SwaggerOperation(Summary = "Get Channels")]
        public IEnumerable<string> GetAllChannels()
            => edi.Player.Channels.ToArray();

        [HttpGet("Assets")]
        [SwaggerOperation(Summary = "Gets the list of available multimedia files in the gallery and uploads.")]
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
        [SwaggerOperation(Summary = "Uploads multimedia files and updates the gallery definitions.")]
        public async Task<ActionResult<IEnumerable<DefinitionResponseDto>>> CreateAssets([FromForm] List<IFormFile> files)
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

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
                uploadedFiles.Add(file.FileName);

            }
            await edi.Init(folderPath);
            return Ok(edi.Definitions.Select(x=> new DefinitionResponseDto(x)));
        }
    }
}
