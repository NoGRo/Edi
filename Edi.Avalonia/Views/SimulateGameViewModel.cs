using System.ComponentModel;
using Edi.Core.Device.Simulator;

namespace Edi.Avalonia.Views;

public class SimulateGameViewModel : INotifyPropertyChanged
{
    private PreviewDevice? simulatorDevice;

    public PreviewDevice? SimulatorDevice
    {
        get => simulatorDevice;
        set
        {
            simulatorDevice = value;
            OnPropertyChanged(nameof(SimulatorDevice));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}