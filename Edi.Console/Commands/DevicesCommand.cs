using System;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Edi.Core;
using Edi.Core.Device.Interfaces;
using Edi.Core.Device;

namespace Edi.Consola.Commands
{

    public static class DevicesCommand
    {
        public static Command Build(IEdi edi)
        {
            var cmd = new Command("devices", "Manage devices such as vibrators or strokers");

            var listCmd = new Command("list", "List all connected devices with their status, variant and range");
            var jsonOpt = new Option<bool>("--json", "Output as JSON instead of human-readable format");
            listCmd.AddOption(jsonOpt);
            listCmd.Handler = CommandHandler.Create<bool>((json) =>
            {
                if (json)
                {
                    var jsonOutput = System.Text.Json.JsonSerializer.Serialize(edi.Devices.Select(x => new DeviceDto
                    {
                        IsReady = x.IsReady,
                        Name = x.Name,
                        Variants = x.Variants.ToArray(),
                        SelectedVariant = x.SelectedVariant,
                        Min = (x as IRange)?.Min ?? 0,
                        Max = (x as IRange)?.Max ?? 100
                    }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    Console.WriteLine(jsonOutput);
                }
                else
                {
                    edi.Devices.ToList().ForEach(d =>
                    {
                        var range = d is IRange r ? $"Range={r.Min}-{r.Max}" : "Range=0-100";
                        var variants = string.Join(", ", d.Variants);
                        Console.WriteLine($"- {d.Name}: Ready={d.IsReady}, Variant={d.SelectedVariant}, Variants=[{variants}], {range}");
                    });
                }
            });
            cmd.AddCommand(listCmd);


            var variantCmd = new Command("variant", "Set the active variant for a specific device") {
                new Argument<string>("device", "Name of the device"),
                new Argument<string>("variant", "Variant to activate")
            };
            variantCmd.Handler = CommandHandler.Create<string, string>(async (device, variant) =>
            {
                var d = edi.Devices.FirstOrDefault(x => x.Name == device);
                if (d == null || !d.Variants.Contains(variant))
                {
                    Console.WriteLine("❌ Device or variant not found"); return;
                }
                await edi.DeviceManager.SelectVariant(d, variant);
                Console.WriteLine($"✅ Variant '{variant}' set on '{device}'");
            });
            cmd.AddCommand(variantCmd);

            var rangeCmd = new Command("range", "Configure stimulation range (0–100) for devices that support it") {
                new Argument<string>("device", "Name of the device"),
                new Argument<int>("min", "Minimum intensity value (0–100)"),
                new Argument<int>("max", "Maximum intensity value (0–100)")
            };
            rangeCmd.Handler = CommandHandler.Create<string, int, int>(async (device, min, max) =>
            {
                var d = edi.Devices.FirstOrDefault(x => x.Name == device);
                if (d is not IRange r || d == null)
                {
                    Console.WriteLine("❌ Device not found or does not support range"); return;
                }
                if (min < 0 || max > 100 || max < min)
                {
                    Console.WriteLine("❌ Range must be between 0 and 100, and min ≤ max"); return;
                }
                await edi.DeviceManager.SelectRange(d, min, max);
                Console.WriteLine($"✅ Range {min}-{max} set on '{device}'");
            });
            cmd.AddCommand(rangeCmd);

            return cmd;
        }


    }
}