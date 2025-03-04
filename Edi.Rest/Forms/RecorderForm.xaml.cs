using Edi.Core;
using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

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
            DataContext = recorder.config; // Bindear RecorderConfig al DataContext
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                recorder.StartRecord();
                txtStatus.Text = "Recording...";
                btnStart.IsEnabled = false;
                btnEnd.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting recording: {ex.Message}");
            }
        }

        private void BtnEnd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                recorder.End();
                txtStatus.Text = "Recording stopped";
                btnStart.IsEnabled = true;
                btnEnd.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error ending recording: {ex.Message}");
            }
        }

        private void BtnCopyFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            string command = "ffmpeg " + recorder.GenerateFfmpegCommand();
            Application.Current.Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(command);
            });
            txtStatus.Text = "FFmpeg command copied to clipboard";
        }
        private double GetDpiScaleFactor()
        {
            // Get the DPI scale factor for the current window
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null)
            {
                return source.CompositionTarget.TransformToDevice.M11; // Horizontal DPI scale
            }
            return 1.0; // Default to 1 if unable to determine
        }
        private void BtnAdjustFrames_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (int.TryParse(txtFrameOffset.Text, out int frameOffset))
                {
                    recorder.AdjustChaptersByFrames(frameOffset);
                    txtStatus.Text = $"Adjusted chapters by {frameOffset} frames";
                }
                else
                {
                    MessageBox.Show("Please enter a valid integer for frame offset.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adjusting frames: {ex.Message}");
            }
        }

        private void BtnSelectArea_Click(object sender, RoutedEventArgs e)
        {
            // Create a transparent window for area selection
            selectionWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Gray,
                Opacity = 0.4,
                Topmost = true,
                WindowState = WindowState.Maximized
            };

            Canvas canvas = new Canvas();
            selectionWindow.Content = canvas;

            selectionRectangle = new Rectangle
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2
            };
            canvas.Children.Add(selectionRectangle);

            Point startPoint = default;
            bool isSelecting = false;

            selectionWindow.MouseLeftButtonDown += (s, args) =>
            {
                startPoint = args.GetPosition(canvas);
                isSelecting = true;
                Canvas.SetLeft(selectionRectangle, startPoint.X);
                Canvas.SetTop(selectionRectangle, startPoint.Y);
            };

            selectionWindow.MouseMove += (s, args) =>
            {
                if (!isSelecting) return;
                Point currentPoint = args.GetPosition(canvas);
                double width = Math.Abs(currentPoint.X - startPoint.X);
                double height = Math.Abs(currentPoint.Y - startPoint.Y);
                selectionRectangle.Width = width;
                selectionRectangle.Height = height;
                Canvas.SetLeft(selectionRectangle, Math.Min(startPoint.X, currentPoint.X));
                Canvas.SetTop(selectionRectangle, Math.Min(startPoint.Y, currentPoint.Y));
            };

            selectionWindow.MouseLeftButtonUp += (s, args) =>
            {
                if (isSelecting)
                {
                    double dpiScale = GetDpiScaleFactor();
                    double x = Canvas.GetLeft(selectionRectangle);
                    double y = Canvas.GetTop(selectionRectangle);
                    double width = selectionRectangle.Width;
                    double height = selectionRectangle.Height;

                    recorder.config.X = (int)(x * dpiScale);
                    recorder.config.Y = (int)(y * dpiScale);
                    recorder.config.Width = (int)(width * dpiScale);
                    recorder.config.Height = (int)(height * dpiScale);

                    selectionWindow.Close();
                }
            };

            selectionWindow.Show();
        }
    }
}