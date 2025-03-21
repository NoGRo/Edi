using Edi.Core;
using Edi.Forms;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Recorder = Edi.Core.Recorder;

namespace Edi.Controllers
{
    [Route("[controller]")]
    public class GameRecorderController : ControllerBase
    {
        private readonly IRecorder Recorder = App.Edi.Recorder;

        [HttpPost("AddChapter/{name}")]
        public IActionResult AddChapter(
            [Required, FromRoute] string name,
            [FromQuery] long seek = 0,
            [FromQuery, Range(0, 100)] int? addPointAtPosition = null)
        {

            Recorder.AddChapter(name, seek, addPointAtPosition);
            return Ok(new { message = $"Chapter '{name}' added" });
            

        }

        [HttpPost("AddPoint/{Position}")]
        public IActionResult AddPoint(
            [FromRoute, Range(0, 100)] int? Position = null, [FromQuery]long Seek = 0)
        {
 
            Recorder.AddPoint(Position ?? 0, Seek);
            return Ok(new { message = "Point added successfully" });

        }

        [HttpPost("EndChapter")]
        public IActionResult EndChapter(
            [FromQuery, Range(0, 100)] int? addPointAtPosition = null, [FromQuery] long Seek = 0)
        {

            Recorder.EndChapter(addPointAtPosition, Seek);
            return Ok(new { message = "Chapter ended successfully" });
        }

        [HttpPost("Start")]
        public IActionResult Start()
        {

            if (Recorder.IsRecording)
            {
                return BadRequest(new { error = "Recording is already in progress" });
            }

            Recorder.Start();
            return Ok(new
            {
                message = "Recording started successfully",
                isRecording = Recorder.IsRecording
            });

        }

        [HttpPost("Stop")]
        public IActionResult Stop()
        {
       
            if (!Recorder.IsRecording)
            {
                return BadRequest(new { error = "No active recording to stop" });
            }

            Recorder.Stop();
            return Ok(new
            {
                message = "Recording stopped successfully",
                isRecording = Recorder.IsRecording
            });
           
        }
    }
}