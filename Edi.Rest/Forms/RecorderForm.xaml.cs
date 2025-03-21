using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace Edi.Forms
{
    public partial class Recorder : Window
    {
        private Core.Recorder recorder;
        private Window selectionWindow;
        private Rectangle selectionRectangle;

        public Recorder()
        {
            InitializeComponent();
            recorder = App.Edi.Recorder;
            DataContext = recorder.config;
            recorder.StatusUpdated += Recorder_StatusUpdated;
            txtStatus.Text = "Recorder ready";
            txtOutputFile.Text = recorder.config.OutputName;
        }

        private void Recorder_StatusUpdated(object sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtStatus.Text = message;
                if (message == "Recording started")
                {
                    txtStatus.Background = Brushes.Green;
                    Task.Run(() => Console.Beep(500, 1000)); // Pitido en un hilo separado
                    Task.Delay(1000).ContinueWith(_ => Dispatcher.Invoke(() => txtStatus.Background = null)); // Restaurar fondo después de 1 segundo
                }
            });
        }

        private async void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!recorder.IsRecording)
                {
                    btnToggle.IsEnabled = false;
                    for (int i = 3; i > 0; i--)
                    {
                        txtStatus.Text = $"Starting in {i}...";
                        await Task.Delay(1000);
                    }
                    await Task.Run(() => recorder.Start()); // Solo inicia la grabación
                    btnToggle.Content = new Rectangle { Width = 15, Height = 15, Fill = Brushes.Black };
                }
                else
                {
                    recorder.Stop();
                    btnToggle.Content = new Ellipse { Width = 15, Height = 15, Fill = Brushes.Red };
                }
                btnToggle.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
                btnToggle.IsEnabled = true;
            }
        }

        private void BtnSelectOutput_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Funscript files (*.funscript)|*.funscript|All files (*.*)|*.*",
                DefaultExt = ".funscript",
                FileName = Path.GetFileName(recorder.config.OutputName),
                InitialDirectory = Path.GetDirectoryName(recorder.config.OutputName) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                recorder.config.OutputName = saveFileDialog.FileName;
                txtOutputFile.Text = saveFileDialog.FileName;
            }
        }

        private void ChkAlwaysOnTop_Checked(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).Topmost = true;
        }

        private void ChkAlwaysOnTop_Unchecked(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).Topmost = false;
        }
    }
}