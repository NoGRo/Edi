using Edi.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace Edi.Core
{
    public class EdiHostedService : IHostedService
    {
        private readonly IEdi _edi;
        private readonly WebApplication _webApp;
        private readonly ILogger<EdiHostedService> _logger;
        
        public EdiHostedService(IEdi edi, ILogger<EdiHostedService> logger, ConfigurationManager Config)
        {
            _edi = edi;
            _logger = logger;
            _webApp = ApiBuilder.BuildApi(Config, _edi);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {

            await _edi.Init();
            _ = _webApp.RunAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Apagando Edi...");
            return Task.CompletedTask;
        }
    }
}
