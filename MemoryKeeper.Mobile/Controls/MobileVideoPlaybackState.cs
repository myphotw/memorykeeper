namespace MemoryKeeper.Mobile.Controls;

public enum MobileVideoPlaybackState
{
    None,
    Preparing,
    Ready,
    Playing,
    Paused,
    Buffering,
    Failed,
}

public sealed record MobileVideoPlaybackRequest(string FileId, long Generation);

public sealed class MobileVideoPlaybackStateChangedEventArgs : EventArgs
{
    public MobileVideoPlaybackStateChangedEventArgs(
        MobileVideoPlaybackRequest request,
        MobileVideoPlaybackState state,
        string? failureType = null)
    {
        Request = request;
        State = state;
        FailureType = failureType;
    }

    public MobileVideoPlaybackRequest Request { get; }

    public MobileVideoPlaybackState State { get; }

    /// <summary>Exception type only; authenticated URLs and messages are deliberately excluded.</summary>
    public string? FailureType { get; }
}
