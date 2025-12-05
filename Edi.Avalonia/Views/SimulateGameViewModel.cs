using Edi.Core.Device.Simulator;
using PropertyChanged.SourceGenerator;

namespace Edi.Avalonia.Views;

public partial class SimulateGameViewModel
{
    [Notify]
    private PreviewDevice? simulatorDevice;
}