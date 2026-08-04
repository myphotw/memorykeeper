namespace MemoryKeeper.App.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync(string pickerTitle);
}
