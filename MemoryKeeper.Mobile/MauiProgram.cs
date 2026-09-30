using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Http;
using MemoryKeeper.Mobile.Security;
using MemoryKeeper.Mobile.ViewModels;
using MemoryKeeper.Mobile.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

namespace MemoryKeeper.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<IMobileBackendConfiguration, MobileBackendConfiguration>();
        builder.Services.AddSingleton<ISecureTokenStore, MauiSecureTokenStore>();
        builder.Services.AddTransient<MobileBackendAuthenticationHandler>();
        builder.Services
            .AddHttpClient(MobileHttpClientNames.Backend)
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .AddHttpMessageHandler<MobileBackendAuthenticationHandler>();

        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<AppShell>();

        return builder.Build();
    }
}
