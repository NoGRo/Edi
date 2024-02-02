using Edi.Core;
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
using System.Windows.Shapes;

namespace Edi.Forms
{
    /// <summary>
    /// Lógica de interacción para SimulateGame.xaml
    /// </summary>
    public partial class SimulateGame : Window
    {
        private readonly IEdi edi = App.Edi;
        public SimulateGame()
        {
            InitializeComponent();

            //SourceItem = edi.Definitions;

        }
        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Play("name");
            });
        }
        private async void PlayRandomButton_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Play("name");
            });
        }
        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Stop();
            });
        }
        private async void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                await edi.Pause();
            });
        }
        
    }
}
