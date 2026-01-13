using Microsoft.Extensions.DependencyInjection;

namespace Edi.Core.Players
{
    public static class PlayerRegistration
    {
        public static void AddPlayers(this IServiceCollection services)
        {
            services.AddSingleton<IPlayerChannels, MultiChannelPlayer>();
            services.AddSingleton(sp => new ChannelManager<IPlayer>(
                        () => new IPlayer[] {
                            sp.GetRequiredService<ReactionGalleryFillerPlayer>(), // Default player for new channels 
                            sp.GetRequiredService<OBSPlayer>()
                        }
                    ));

            services.AddTransient<ReactionGalleryFillerPlayer>();
            services.AddSingleton<OBSPlayer>();
            services.AddTransient<DevicePlayer>();
           
            services.AddSingleton<SyncPlaybackFactory>();
            services.AddSingleton<PlayerLogService>();
        }
    }
}
