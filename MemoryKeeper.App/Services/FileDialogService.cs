using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MemoryKeeper.App.Services;

public sealed class FileDialogService : IFileDialogService
{
    public async Task<string?> PickSaveZipAsync(string suggestedFileName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName
        };
        picker.FileTypeChoices.Add("MemoryKeeper Backup", [".zip"]);

        Initialize(picker);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickOpenZipAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add(".zip");

        Initialize(picker);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private static void Initialize(object picker)
    {
        var window = (Microsoft.UI.Xaml.Application.Current as App)?.MainWindow;
        if (window is null)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(picker, hwnd);
    }
}
