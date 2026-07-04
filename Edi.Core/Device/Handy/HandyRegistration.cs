using Edi.Core.Device.Interfaces;
using Edi.Core.Device.Handy.Transport;
using Edi.Core.Device.Handy.Transport.BLE;
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
        /// <summary>
        /// Registers Handy device provider with HTTP/REST transport (default).
        /// </summary>
        public static void AddHandy(this IServiceCollection services)
        {
            AddHandyWithHttpTransport(services);
        }

        /// <summary>
        /// Registers Handy device provider with HTTP/REST transport.
        /// </summary>
        public static void AddHandyWithHttpTransport(this IServiceCollection services)
        {
            services.AddSingleton<IDeviceProvider, HandyProvider>();
            services.AddHttpClient("HandyAPI", client =>
            {
                client.BaseAddress = new Uri("https://www.handyfeeling.com/api/handy-rest/");
                client.Timeout = TimeSpan.FromSeconds(30);
            }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        }

        /// <summary>
        /// Registers Handy device provider with BLE (Bluetooth Low Energy) transport.
        /// Requires BLE hardware support and Windows.Devices.Bluetooth or InTheHand.BluetoothLE implementation.
        /// </summary>
        public static void AddHandyWithBleTransport(this IServiceCollection services, BleHandyOptions? options = null)
        {
            services.AddSingleton<IDeviceProvider, HandyProvider>();
            services.AddSingleton(options ?? new BleHandyOptions());
            
            // Still register HTTP client for fallback or compatibility
            services.AddHttpClient("HandyAPI", client =>
            {
                client.BaseAddress = new Uri("https://www.handyfeeling.com/api/handy-rest/");
                client.Timeout = TimeSpan.FromSeconds(30);
            }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        }

        /// <summary>
        /// Registers both HTTP and BLE transports, allowing runtime selection via HandyProvider configuration.
        /// </summary>
        public static void AddHandyWithDualTransport(this IServiceCollection services, BleHandyOptions? bleOptions = null)
        {
            services.AddSingleton<IDeviceProvider, HandyProvider>();
            services.AddSingleton(bleOptions ?? new BleHandyOptions());
            
            services.AddHttpClient("HandyAPI", client =>
            {
                client.BaseAddress = new Uri("https://www.handyfeeling.com/api/handy-rest/");
                client.Timeout = TimeSpan.FromSeconds(30);
            }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        }
    }
}
