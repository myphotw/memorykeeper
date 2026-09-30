using MemoryKeeper.Mobile.Views;
using MemoryKeeper.Mobile.Diagnostics;

namespace MemoryKeeper.Mobile;

public sealed class AppShell : Shell
{
    public AppShell(HomePage homePage)
    {
        MobileStartupCheckpoint.Record("APP-SHELL-CONSTRUCTOR");
        FlyoutBehavior = FlyoutBehavior.Disabled;
        Items.Add(new ShellContent
        {
            Title = "홈",
            Route = "home",
            Content = homePage,
        });
        MobileStartupCheckpoint.Record("APP-SHELL-CONSTRUCTOR-DONE");
    }
}
