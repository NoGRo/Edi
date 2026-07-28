using Microsoft.Extensions.DependencyInjection;

namespace Edi.Core.Players
{
    public static class PlayerRegistration
    {
        public static void AddPlayers(this IServiceCollection services)
        {
            services.AddSingleton(sp => new ChannelManager<IPlayer>(
                        () => sp.GetRequiredService<ReactionGalleryFillerPlayer>()
                    ));

            services.AddTransient<ReactionGalleryFillerPlayer>();
            services.AddTransient<DevicePlayer>();
            services.AddSingleton<MultiChannelPlayer>();
            services.AddSingleton<OBSPlayer>();
            services.AddSingleton<IPlayerChannels>(
                sp => sp.GetRequiredService<OBSPlayer>());

            services.AddSingleton<SyncPlaybackFactory>();
            services.AddSingleton<PlayerLogService>();
        }
    }
}
