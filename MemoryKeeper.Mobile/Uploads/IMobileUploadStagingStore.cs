namespace MemoryKeeper.Mobile.Uploads;

public interface IMobileUploadStagingStore
{
    Task<MobileSelectedMediaFile> StageAsync(
        string fileName,
        string contentType,
        Stream originalStream,
        CancellationToken cancellationToken = default);
}
