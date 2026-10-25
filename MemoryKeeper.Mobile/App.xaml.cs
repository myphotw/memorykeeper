using Microsoft.Extensions.DependencyInjection;
using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Diagnostics;

namespace MemoryKeeper.Mobile;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IMobileBackendConnectionMonitor? _connectionMonitor;

    public App(IServiceProvider services)
    {
        MobileStartupCheckpoint.Record("APP-CONSTRUCTOR");
        InitializeComponent();
        _connectionMonitor = services.GetService<IMobileBackendConnectionMonitor>();

        try
        {
            MobileStartupCheckpoint.Record("APP-SHELL");
            MainPage = services.GetRequiredService<AppShell>();
            MobileStartupCheckpoint.Record("APP-SHELL-DONE");
        }
        catch (Exception exception)
        {
            MobileStartupCheckpoint.Record("APP-SHELL-CAUGHT");
            MainPage = CreateStartupFailurePage(exception);
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);
        window.Activated += OnWindowActivated;
        window.Resumed += OnWindowResumed;
        window.Stopped += OnWindowStopped;
        window.Destroying += OnWindowDestroying;
        return window;
    }

    private void OnWindowActivated(object? sender, EventArgs e) =>
        _connectionMonitor?.SetForeground(true);

    private void OnWindowResumed(object? sender, EventArgs e) =>
        _connectionMonitor?.SetForeground(true);

    private void OnWindowStopped(object? sender, EventArgs e) =>
        _connectionMonitor?.SetForeground(false);

    private void OnWindowDestroying(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Activated -= OnWindowActivated;
            window.Resumed -= OnWindowResumed;
            window.Stopped -= OnWindowStopped;
            window.Destroying -= OnWindowDestroying;
        }

        _connectionMonitor?.SetForeground(false);
    }

    private static Page CreateStartupFailurePage(Exception exception)
    {
        var message = "MemoryKeeper를 시작하는 중 문제가 발생했습니다. 앱을 다시 실행해 주세요.";
#if DEBUG
        var rootException = exception.GetBaseException();
        message += $"\n\n진단: APP-SHELL / {rootException.GetType().Name}";
#endif

        return new ContentPage
        {
            BackgroundColor = Color.FromArgb("#F5F9FF"),
            Content = new Grid
            {
                Padding = new Thickness(32),
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 12,
                        VerticalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new Label
                            {
                                Text = "MemoryKeeper",
                                FontSize = 28,
                                FontAttributes = FontAttributes.Bold,
                                HorizontalTextAlignment = TextAlignment.Center,
                                TextColor = Color.FromArgb("#14213D"),
                            },
                            new Label
                            {
                                Text = message,
                                FontSize = 15,
                                HorizontalTextAlignment = TextAlignment.Center,
                                TextColor = Color.FromArgb("#5F6B7A"),
                            },
                        },
                    },
                },
            },
        };
    }
}
