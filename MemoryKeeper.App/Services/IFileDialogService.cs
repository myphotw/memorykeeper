namespace MemoryKeeper.App.Services;

public interface IFileDialogService
{
    Task<string?> PickSaveZipAsync(string suggestedFileName);

    Task<string?> PickOpenZipAsync();
}
