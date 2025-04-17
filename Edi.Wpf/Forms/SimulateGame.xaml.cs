using System.ComponentModel;
using System.Windows;
using Edi.Core;
using Edi.Core.Device.Simulator;
using Edi.Core.Gallery.Funscript;

namespace Edi.Forms
{
    public partial class SimulateGame : Window
    {
        private readonly IEdi edi = App.Edi;
        private SimulatorDevice SimulatorDevice;

        public SimulateGame()
        {
            InitializeComponent();
            SimulatorDevice = new SimulatorDevice(edi.GetRepository<FunscriptRepository>(), edi.Logger);
            this.DataContext = new { SimulatorDevice };

            this.Loaded += SimulateGame_Loaded;
            this.Closing += SimulateGame_Closing;

            // Cargar posición guardada

        }

        private void SimulateGame_Loaded(object sender, RoutedEventArgs e)
        {
            edi.DeviceManager.LoadDevice(SimulatorDevice);
        }

        private void SimulateGame_Closing(object sender, CancelEventArgs e)
        {
            SimulatorDevice?.StopGallery();
            if (SimulatorDevice != null && edi.DeviceManager != null)
            {
                edi.DeviceManager.UnloadDevice(SimulatorDevice);
            }

            SimulatorDevice = null;
        }
    }
}