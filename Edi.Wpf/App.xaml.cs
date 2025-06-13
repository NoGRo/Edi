using Edi.Core;
using Edi.Core.Gallery;
using Microsoft.AspNetCore.StaticFiles;

using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Threading.Tasks;
using System.Collections.Generic;

using System.Net;
using Microsoft.Extensions.Logging;


namespace Edi.Forms
{
    /// <summary>
    /// Interaction lógica para App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IEdi Edi { get; private set; }
        public static IServiceProvider ServiceProvider { get; private set; }

        public App(IEdi edi, IServiceProvider serviceProvider)
        {
            Edi = edi;
            ServiceProvider = serviceProvider;
        }


        public App()
        {
        
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            
            var mainWindow = new MainWindow();

            mainWindow.Show();
            base.OnStartup(e);
            // Ejecuta el servidor web en un hilo separado para no bloquear la interfaz de usuario

        }
    }
}
