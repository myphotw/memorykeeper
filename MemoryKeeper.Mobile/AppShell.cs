using MemoryKeeper.Mobile.Views;

namespace MemoryKeeper.Mobile;

public sealed class AppShell : Shell
{
    public AppShell(HomePage homePage)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        Items.Add(new ShellContent
        {
            Title = "홈",
            Route = "home",
            Content = homePage,
        });
    }
}
