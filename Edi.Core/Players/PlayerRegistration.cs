using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Edi.Core.Players
{
    public static class PlayerRegistration
    {
        public static void AddPlayers(this IServiceCollection services)
        {
            services.AddSingleton<IPlayerChannels, MultiChannelPlayer>();
            services.AddSingleton(sp => new ChannelManager<IPlayer>(
                        () => sp.GetRequiredService<ReactionGalleryFillerPlayer>()// Default player for new channels
                    ));

            services.AddTransient<ReactionGalleryFillerPlayer>();
            services.AddTransient<DevicePlayer>();
           
            services.AddSingleton<SyncPlaybackFactory>();
            services.AddSingleton<PlayerLogService>();
        }
    }
}
