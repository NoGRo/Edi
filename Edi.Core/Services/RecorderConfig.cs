using System;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Edi.Core
{
    public class RecorderConfig : INotifyPropertyChanged
    {
        private int x = 352;
        private int y = 1055;
        private int width = 760;
        private int height = 1040;
        private int frameRate = 30;
        private string outputName = @"D:\Juegos\MaxTheElf\Edi\Gallery\output.mp4";
        private string ffmpegCodec = "-f dshow -i audio=\"CABLE Output (VB-Audio Virtual Cable)\" -c:v h264_nvenc -preset p4 -b:v 6M -c:a aac -b:a 128k";
        private string ffmpegFullCommand;

        public event PropertyChangedEventHandler PropertyChanged;

        // Propiedades principales
        public int X { get => x; set { x = value; UpdateCommand(); OnPropertyChanged(nameof(X)); } }
        public int Y { get => y; set { y = value; UpdateCommand(); OnPropertyChanged(nameof(Y)); } }
        public int Width { get => width; set { width = value; UpdateCommand(); OnPropertyChanged(nameof(Width)); } }
        public int Height { get => height; set { height = value; UpdateCommand(); OnPropertyChanged(nameof(Height)); } }
        public int FrameRate { get => frameRate; set { frameRate = value; UpdateCommand(); OnPropertyChanged(nameof(FrameRate)); } }
        public string OutputName { get => outputName; set { outputName = value; UpdateCommand(); OnPropertyChanged(nameof(OutputName)); } }

        // Opciones adicionales
        public string FffmpegCodec
        {
            get => ffmpegCodec;
            set { ffmpegCodec = value; UpdateCommand(); OnPropertyChanged(nameof(FffmpegCodec)); }
        }

        // Comando completo
        public string FfmpegFullCommand
        {
            get => ffmpegFullCommand;
            set
            {
                ffmpegFullCommand = value;
                ParseCommand(value);
                OnPropertyChanged(nameof(FfmpegFullCommand));
            }
        }

        // Generar comando con la concatenación ajustada
        private void UpdateCommand()
        {
            ffmpegFullCommand = $"ffmpeg -y -f gdigrab -progress pipe:1 -framerate {FrameRate} -offset_x {X} -offset_y {Y} " +
                                $"-video_size {Width}x{Height} -i desktop {FffmpegCodec} \"{OutputName}\"";
            OnPropertyChanged(nameof(FfmpegFullCommand));
        }

        // Parsear comando para actualizar campos
        private void ParseCommand(string command)
        {
            if (string.IsNullOrEmpty(command) || !command.StartsWith("ffmpeg -y -f gdigrab -progress pipe:1"))
            {
                UpdateCommand();
                return;
            }

            // Expresiones regulares para cada parte
            var framerateMatch = Regex.Match(command, @"-framerate (\d+)");
            var offsetXMatch = Regex.Match(command, @"-offset_x (\d+)");
            var offsetYMatch = Regex.Match(command, @"-offset_y (\d+)");
            var sizeMatch = Regex.Match(command, @"-video_size (\d+)x(\d+)");
            var desktopMatch = Regex.Match(command, @"-i desktop");
            var outputMatch = Regex.Match(command, @"""([^""]+)""$"); // Última parte entre comillas

            // Extraer valores
            if (framerateMatch.Success) frameRate = int.Parse(framerateMatch.Groups[1].Value);
            if (offsetXMatch.Success) x = int.Parse(offsetXMatch.Groups[1].Value);
            if (offsetYMatch.Success) y = int.Parse(offsetYMatch.Groups[1].Value);
            if (sizeMatch.Success)
            {
                width = int.Parse(sizeMatch.Groups[1].Value);
                height = int.Parse(sizeMatch.Groups[2].Value);
            }
            if (outputMatch.Success) outputName = outputMatch.Groups[1].Value;

            // Extraer FffmpegCodec (todo entre "-i desktop" y el nombre del archivo de salida)
            ffmpegCodec = "";
            if (desktopMatch.Success && outputMatch.Success)
            {
                int codecStart = desktopMatch.Index + desktopMatch.Length;
                int outputStart = outputMatch.Index;
                if (codecStart < outputStart)
                {
                    ffmpegCodec = command.Substring(codecStart, outputStart - codecStart).Trim();
                }
            }

            // Notificar cambios
            OnPropertyChanged(nameof(X));
            OnPropertyChanged(nameof(Y));
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
            OnPropertyChanged(nameof(FrameRate));
            OnPropertyChanged(nameof(OutputName));
            OnPropertyChanged(nameof(FffmpegCodec));
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string GenerateFfmpegCommand() => FfmpegFullCommand;

        public RecorderConfig()
        {
            UpdateCommand();
        }
    }
}