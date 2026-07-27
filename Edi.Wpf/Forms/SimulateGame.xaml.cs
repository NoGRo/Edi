using System.ComponentModel;
using System.Windows;
using Edi.Core;
using Edi.Core.Device;
using Edi.Core.Device.Simulator;
using Edi.Core.Gallery.Definition;
using Edi.Core.Gallery.Funscript;
using Edi.Core.Services;

namespace Edi.Forms
{
    public partial class SimulateGame : Window
    {
        private readonly IEdi edi = App.Edi;
        private readonly DeviceCollector deviceCollector;
        private readonly PreviewWindowConfig windowConfig;
        private PreviewDevice SimulatorDevice;

        public SimulateGame()
        {
            InitializeComponent();
            windowConfig = edi.ConfigurationManager.Get<PreviewWindowConfig>();
            RestoreWindowPlacement();
            SimulatorDevice = new PreviewDevice(
                App.ServiceProvider.GetRequiredService<FunscriptRepository>(),
                App.ServiceProvider.GetRequiredService<DefinitionRepository>(),
                App.ServiceProvider.GetRequiredService<ILogger<PreviewDevice>>());
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
            SaveWindowPlacement();
            SimulatorDevice?.StopGallery();
            if (SimulatorDevice != null && deviceCollector != null)
            {
                deviceCollector.UnloadDevice(SimulatorDevice);
            }

            SimulatorDevice = null;
        }

        private void RestoreWindowPlacement()
        {
            var width = windowConfig.Width ?? Width;
            var height = windowConfig.Height ?? Height;
            if (double.IsFinite(width) && width >= MinWidth)
                Width = width;
            if (double.IsFinite(height) && height >= MinHeight)
                Height = height;

            if (windowConfig.Left is not double left
                || windowConfig.Top is not double top
                || !IsPlacementVisible(left, top, Width, Height))
            {
                return;
            }

            Left = left;
            Top = top;
        }

        private void SaveWindowPlacement()
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;

            if (!double.IsFinite(bounds.Left)
                || !double.IsFinite(bounds.Top)
                || !double.IsFinite(bounds.Width)
                || !double.IsFinite(bounds.Height))
            {
                return;
            }

            windowConfig.Left = bounds.Left;
            windowConfig.Top = bounds.Top;
            windowConfig.Width = bounds.Width;
            windowConfig.Height = bounds.Height;
            edi.ConfigurationManager.Save(windowConfig);
        }

        private static bool IsPlacementVisible(
            double left,
            double top,
            double width,
            double height)
        {
            if (!double.IsFinite(left)
                || !double.IsFinite(top)
                || !double.IsFinite(width)
                || !double.IsFinite(height))
            {
                return false;
            }

            var savedBounds = new Rect(left, top, width, height);
            var virtualDesktop = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
            savedBounds.Intersect(virtualDesktop);
            return savedBounds.Width >= 50 && savedBounds.Height >= 50;
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

    [UserConfig]
    public class PreviewWindowConfig
    {
        public double? Left { get; set; }
        public double? Top { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
    }
}
