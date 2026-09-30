namespace MemoryKeeper.Mobile;

public partial class App : Microsoft.Maui.Controls.Application
{
    public App(AppShell shell)
    {
        InitializeComponent();
        MainPage = shell;
    }
}
