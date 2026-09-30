using Microsoft.Extensions.DependencyInjection;
using MemoryKeeper.Mobile.Diagnostics;

namespace MemoryKeeper.Mobile;

public partial class App : Microsoft.Maui.Controls.Application
{
    public App(IServiceProvider services)
    {
        MobileStartupCheckpoint.Record("APP-CONSTRUCTOR");
        InitializeComponent();

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
