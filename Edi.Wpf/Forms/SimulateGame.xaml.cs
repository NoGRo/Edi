using System.ComponentModel;
using System.Windows;
using Edi.Core;
using Edi.Core.Device;
using Edi.Core.Device.Simulator;
using Edi.Core.Gallery.Funscript;

namespace Edi.Forms
{
    public partial class SimulateGame : Window
    {
        private readonly IEdi edi = App.Edi;
        private readonly DeviceCollector deviceCollector;
        private PreviewDevice SimulatorDevice;

        public SimulateGame()
        {
            InitializeComponent();
            SimulatorDevice = new PreviewDevice(App.ServiceProvider.GetRequiredService<FunscriptRepository>(), App.ServiceProvider.GetRequiredService<ILogger<PreviewDevice>>());
            this.DataContext = new { SimulatorDevice };

            this.Loaded += SimulateGame_Loaded;
            this.Closing += SimulateGame_Closing;
            this.deviceCollector = edi.DeviceCollector;

            // Cargar posición guardada

        }
        private void SimulateGame_Loaded(object sender, RoutedEventArgs e)
        {
            deviceCollector.LoadDevice(SimulatorDevice);
        }

        private void SimulateGame_Closing(object sender, CancelEventArgs e)
        {
            SimulatorDevice?.StopGallery();
            if (SimulatorDevice != null && deviceCollector != null)
            {
                deviceCollector.UnloadDevice(SimulatorDevice);
            }

            SimulatorDevice = null;
        }

        internal void OnAlwaysOnTopChecked(object sender, RoutedEventArgs e)
        {
            this.Topmost = true;
        }

        internal void OnAlwaysOnTopUnchecked(object sender, RoutedEventArgs e)
        {
            this.Topmost = false;
        }
    }
}