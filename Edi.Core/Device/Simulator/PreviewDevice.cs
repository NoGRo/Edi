using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.Funscript;
using Microsoft.Extensions.Logging;

namespace Edi.Core.Device.Simulator;

public class PreviewDevice : SimulatorDevice
{
    public PreviewDevice(
        FunscriptRepository repository,
        DefinitionRepository definitionRepository,
        ILogger<PreviewDevice> logger)
        : base(repository, definitionRepository, logger)
    {
        Name = "Preview Device";
        logger.LogInformation("ProgressBarSimulator initialized.");
    }
}
