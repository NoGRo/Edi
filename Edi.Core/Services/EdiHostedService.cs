using Edi.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace Edi.Core
{
    public class EdiHostedService : BackgroundService
    {
        private readonly IEdi _edi;
        private readonly WebApplication _webApp;
        private readonly ILogger<EdiHostedService> _logger;
        
        public EdiHostedService(IEdi edi, ILogger<EdiHostedService> logger, ConfigurationManager Config)
            : this(edi, logger, ApiBuilder.BuildApi(Config, edi)) { }

        internal EdiHostedService(IEdi edi, ILogger<EdiHostedService> logger, WebApplication webApp)
        {
            _edi = edi;
            _logger = logger;
            _webApp = webApp;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await using var app = _webApp;
            await _webApp.StartAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            try
            {
                do
                {
                    try { _edi.LaunchGame(automatic: true); }
                    catch (Exception ex) { _logger.LogError(ex, "Could not launch the configured game; will retry while ready."); }
                } while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            finally
            {
                _logger.LogInformation("Apagando Edi...");
                await _webApp.StopAsync(CancellationToken.None);
            }
        }
    }
}
