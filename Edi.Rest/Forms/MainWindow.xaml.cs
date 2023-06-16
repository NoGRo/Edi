using Edi.Core;
using Edi.Core.Device.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Edi.Forms
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IEdi edi =  App.Edi;
        public EdiConfig config;
        public MainWindow()
        {
            config = edi.ConfigurationManager.Get<EdiConfig>();
            this.DataContext = new { config= config,};
            InitializeComponent();

            edi.DeviceManager.OnloadDevice += DeviceManager_OnloadDevice;
            edi.DeviceManager.OnUnloadDevice += DeviceManager_OnUnloadDevice;
            edi.OnChangeStatus += Edi_OnChangeStatus    ;
            LoadForm();
        }

        private void Edi_OnChangeStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                lblStatus.Content = message;
            });
        }

        private void DeviceManager_OnUnloadDevice(Core.Device.Interfaces.IDevice device)
        {
            Dispatcher.Invoke(() =>
            {
                //DevicesGrid.Items.Clear();
                DevicesGrid.ItemsSource = edi.DeviceManager.Devices;
            });

        }

        private void DeviceManager_OnloadDevice(Core.Device.Interfaces.IDevice device)
        {
            Dispatcher.Invoke(() =>
            {
                //DevicesGrid.Items.Clear();
                DevicesGrid.ItemsSource = edi.DeviceManager.Devices;
            });
        }

        private void LoadForm()
        {
            DevicesGrid.ItemsSource = edi.DeviceManager.Devices;
        }

        private void Variants_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var device = (IDevice)e.Source;
            edi.DeviceManager.SelectVariant(device.Name, device.SelectedVariant);
        }

    
    }
}
