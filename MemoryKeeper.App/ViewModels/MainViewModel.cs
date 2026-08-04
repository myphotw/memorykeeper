using CommunityToolkit.Mvvm.ComponentModel;

namespace MemoryKeeper.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string statusMessage = string.Empty;

    public string Title => "Memory Keeper";

    public void ApplyDatabaseStatus(string summary)
    {
#if DEBUG
        StatusMessage = summary;
#else
        _ = summary;
        StatusMessage = string.Empty;
#endif
    }

    public void SetUiStatus(string message)
    {
#if DEBUG
        StatusMessage = message;
#else
        _ = message;
#endif
    }
}
