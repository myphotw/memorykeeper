namespace MemoryKeeper.Mobile.Uploads;

public interface IMobileMediaPicker
{
    Task<IReadOnlyList<MobileSelectedMediaFile>> PickMultipleAsync(
        CancellationToken cancellationToken = default);
}
