using Edi.Core.Device.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Device.Handy
{
    public static class HandyRegistration
    {
        public static void AddHandy(this IServiceCollection services)
        {
            services.AddSingleton<IDeviceProvider, HandyProvider>();
            services.AddHttpClient("HandyAPI", client =>
            {
                client.BaseAddress = new Uri("https://www.handyfeeling.com/api/handy/v2/");
                client.Timeout = TimeSpan.FromSeconds(30);
            }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

        }
    }
}
