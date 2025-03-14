using Microsoft.Extensions.DependencyInjection;

using StreamMaster.Streams.Channels;
using StreamMaster.Streams.Domain;
using StreamMaster.Streams.Factories;
using StreamMaster.Streams.Plugins;
using StreamMaster.Streams.Services;
using StreamMaster.Streams.Streams;

namespace StreamMaster.Streams;

public static class ConfigureServices
{
    public static IServiceCollection AddStreamsServices(this IServiceCollection services)
    {
        services.AddSingleton<IChannelManager, ChannelManager>();
        services.AddSingleton<IChannelService, ChannelService>();
        services.AddSingleton<IStreamFactory, StreamFactory>();
        services.AddTransient<ICommandExecutor, CommandExecutor>();
        services.AddTransient<ICustomPlayListStream, CustomPlayListStream>();
        services.AddTransient<IMultiViewPlayListStream, MultiViewPlayListStream>();
        services.AddTransient<IHTTPStream, HTTPStream>();
        services.AddTransient<ICommandStream, CommandStream>();
        services.AddSingleton<ISourceBroadcasterService, SourceBroadcasterService>();
        services.AddSingleton<IChannelLockService, ChannelLockService>();

        //services.AddSingleton<IHLSManager, HLSManager>();
        services.AddSingleton<IStreamTracker, StreamTracker>();
        services.AddSingleton<IAccessTracker, AccessTracker>();
        //services.AddHostedService<InActiveStreamMonitor>();
        services.AddSingleton<ISwitchToNextStreamService, SwitchToNextStreamService>();
        //services.AddSingleton<IM3U8Generator, M3U8Generator>();
        services.AddSingleton<IChannelBroadcasterService, ChannelBroadcasterService>();
        services.AddSingleton<IClientConfigurationService, ClientConfigurationService>();
        services.AddSingleton<IVideoInfoService, VideoInfoService>();

        services.AddSingleton<IStreamLimitsService, StreamLimitsService>();
        services.AddScoped<IVideoService, VideoService>();
        services.AddSingleton<ICacheManager, CacheManager>();

        services.AddTransient<IMetricsService, MetricsService>();
        services.AddTransient<ISourceProcessingService, SourceProcessingService>();
        return services;
    }
}
