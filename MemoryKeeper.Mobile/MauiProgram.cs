using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Controls;
using MemoryKeeper.Mobile.Diagnostics;
using MemoryKeeper.Mobile.Handlers;
using MemoryKeeper.Mobile.Http;
using MemoryKeeper.Mobile.Images;
using MemoryKeeper.Mobile.Platforms.Android.Images;
using MemoryKeeper.Mobile.ViewModels;
using MemoryKeeper.Mobile.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

namespace MemoryKeeper.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MobileStartupCheckpoint.Record("MAUI-CREATE-START");
        var builder = MauiApp.CreateBuilder();
        MobileStartupCheckpoint.Record("MAUI-BUILDER-CREATED");
        builder
            .UseMauiApp<App>()
            .ConfigureMauiHandlers(handlers =>
            {
                handlers.AddHandler<MobileZoomablePreviewView, MobileZoomablePreviewViewHandler>();
                handlers.AddHandler<MobileVideoPlayerView, MobileVideoPlayerViewHandler>();
                handlers.AddHandler<MobileThumbnailImage, MobileThumbnailImageHandler>();
            });
        MobileStartupCheckpoint.Record("MAUI-APP-REGISTERED");

        builder.Services.AddSingleton<IMobileBackendConfiguration, MobileBackendConfiguration>();
        builder.Services.AddSingleton<IMobileNetworkChangeSource, MauiMobileNetworkChangeSource>();
        builder.Services.AddSingleton<IMobileBackendEndpointResolver, MobileBackendEndpointResolver>();
        MobileStartupCheckpoint.Record("MAUI-CONFIG-REGISTERED");
        builder.Services.AddTransient<MobileBackendAuthenticationHandler>();
        builder.Services.AddTransient<MobileBackendEndpointFailureHandler>();
        builder.Services
            .AddHttpClient(MobileHttpClientNames.Probe)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
            });
        builder.Services
            .AddHttpClient(MobileHttpClientNames.Backend)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .AddHttpMessageHandler<MobileBackendAuthenticationHandler>()
            .AddHttpMessageHandler<MobileBackendEndpointFailureHandler>();
        MobileStartupCheckpoint.Record("MAUI-HTTP-REGISTERED");

        builder.Services.AddSingleton<IFastGalleryApiRepository, MobileFastGalleryApiRepository>();
        builder.Services.AddSingleton<FastGalleryPagingService>();
        builder.Services.AddSingleton<BoundedThumbnailMemoryCache>();
        builder.Services.AddSingleton<AndroidDecodedThumbnailCache>();
        builder.Services.AddSingleton<IMobileThumbnailSourceFactory, MobileThumbnailSourceFactory>();
        builder.Services.AddSingleton<IMobilePreviewSourceFactory, MobilePreviewSourceFactory>();
        MobileStartupCheckpoint.Record("MAUI-GALLERY-REGISTERED");
        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<AppShell>();
        MobileStartupCheckpoint.Record("MAUI-UI-REGISTERED");

        MobileStartupCheckpoint.Record("MAUI-BUILD-START");
        var app = builder.Build();
        MobileStartupCheckpoint.Record("MAUI-BUILD-DONE");
        return app;
    }
}
