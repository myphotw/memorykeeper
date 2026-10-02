namespace MemoryKeeper.Mobile.Images;

public sealed record MobilePreviewLoadResult(
    byte[]? Bytes,
    string RequestDescription,
    string? FailureStage)
{
    public bool IsSuccess => Bytes is { Length: > 0 };

    public static MobilePreviewLoadResult Success(byte[] bytes, string requestDescription) =>
        new(bytes, requestDescription, null);

    public static MobilePreviewLoadResult Failure(string requestDescription, string failureStage) =>
        new(null, requestDescription, failureStage);
}
