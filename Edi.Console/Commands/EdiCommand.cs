using System;

using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Edi.Core;
using Edi.Core.Device.Interfaces;


namespace Edi.Consola.Commands
{
    public static class EdiCommand
    {
        public static Command Build(IEdi edi)
        {
            var cmd = new Command("edi", "Control playback of stimulation galleries or media sequences");

            var play = new Command("play", "Play a gallery or stimulation routine") {
                new Argument<string>("name", "Name of the gallery to play"),
                    new Option<long>("--seek", "Seek position in milliseconds (default: 0)") { IsRequired = false }
            };
            play.Handler = CommandHandler.Create<string, long>(async (name, seek) => {
                await edi.Player.Play(name, seek);
                Console.WriteLine($"▶️ Player.Playing '{name}' from {seek}ms");
            });
            cmd.AddCommand(play);

            var stop = new Command("stop", "Stop playback entirely")
            {
                Handler = CommandHandler.Create(async () => {

                    await edi.Player.Stop();
                    Console.WriteLine("⏹️ Playback stopped");
                })
            };
            cmd.AddCommand(stop);

            var pause = new Command("pause", "Pause the current playback")
            {
                new Option<bool>("--untilResume", "Pause all playback until Resume command") { IsRequired = false }
            };
            pause.Handler = CommandHandler.Create(async () =>
            {
                await edi.Player.Pause();
                Console.WriteLine("⏸️ Playback paused");
            });
            
            cmd.AddCommand(pause);

            var resume = new Command("resume", "Resume playback from paused state") {
                new Option<bool>("--atCurrentTime", "Resume at current playback time") { IsRequired = false }
            };
            resume.Handler = CommandHandler.Create<bool>(async atCurrentTime => {
                await edi.Player.Resume(atCurrentTime);
                Console.WriteLine($"▶️ Resumed (at current time: {atCurrentTime})");
            });
            cmd.AddCommand(resume);

            var intensity = new Command("intensity", "Set maximum intensity (0–100) of the stimulation") {
                new Argument<int>("max", "Maximum intensity value")
            };
            intensity.Handler = CommandHandler.Create<int>(async max => {
                if (max < 0 || max > 100)
                {
                    Console.WriteLine("❌ Intensity must be between 0 and 100");
                    return;
                }
                await edi.Player.Intensity(max);
                Console.WriteLine($"🌡️ Intensity set to {max}%");
            });
            cmd.AddCommand(intensity);

            var definitions = new Command("definitions", "List all available gallery definitions") {
                new Option<bool>("--json", "Output definitions in JSON format")
            };
            definitions.Handler = CommandHandler.Create<bool>((json) => {
                var defs = edi.Definitions.ToArray();
                if (json)
                {
                    var output = JsonSerializer.Serialize(defs, new JsonSerializerOptions { WriteIndented = true });
                    Console.WriteLine(output);
                }
                else
                {
                    foreach (var def in defs)
                    {
                        Console.WriteLine($"- {def.Name} ({def.Type}) → {def.FileName} [{def.StartTime}–{def.EndTime}ms]");
                    }
                }
            });
            cmd.AddCommand(definitions);

            return cmd;
        }
    }

}
