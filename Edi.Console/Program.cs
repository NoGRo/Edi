using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.CommandLine;
using Edi.Core.Device.Interfaces;
using Edi.Consol.Commands;
using Edi.Core;

// Configuración de DI
var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services => {
        services.AddEdi(".\\config.json");
    })
    .Build();

// Obtener el servicio principal
var edi = host.Services.GetRequiredService<IEdi>();

// Comandos raíz
var root = new RootCommand("Edi CLI - Command line control for Edi stimulation system") {
    DevicesCommand.Build(edi),
    EdiCommand.Build(edi)
};

// Ejecutar
return await root.InvokeAsync(args);
