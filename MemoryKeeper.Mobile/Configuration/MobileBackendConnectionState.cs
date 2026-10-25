namespace MemoryKeeper.Mobile.Configuration;

public enum MobileBackendConnectionPhase
{
    Unknown,
    Checking,
    Connected,
    Failed,
}

public enum MobileBackendEndpointKind
{
    None,
    Internal,
    External,
    Single,
}

public enum MobileBackendFailureKind
{
    None,
    Network,
    Timeout,
    Authentication,
    Tls,
    Server,
    ApiContract,
    Configuration,
}

public sealed record MobileBackendConnectionState(
    MobileBackendConnectionPhase Phase,
    MobileBackendEndpointKind EndpointKind = MobileBackendEndpointKind.None,
    MobileBackendFailureKind FailureKind = MobileBackendFailureKind.None,
    Uri? BaseUri = null)
{
    public static MobileBackendConnectionState Unknown { get; } = new(
        MobileBackendConnectionPhase.Unknown);
}

public sealed class MobileBackendConnectionChangedEventArgs(
    MobileBackendConnectionState state) : EventArgs
{
    public MobileBackendConnectionState State { get; } = state;
}

public interface IMobileBackendConnectionMonitor
{
    MobileBackendConnectionState ConnectionState { get; }

    event EventHandler<MobileBackendConnectionChangedEventArgs>? ConnectionChanged;

    Task<Uri> ReconnectAsync(CancellationToken cancellationToken = default);

    void SetForeground(bool isForeground);

    void ReportFailure(MobileBackendFailureKind failureKind);
}

public sealed class MobileBackendConnectionException(
    MobileBackendFailureKind failureKind,
    Exception? innerException = null)
    : HttpRequestException("No configured MemoryKeeper Backend endpoint is reachable.", innerException)
{
    public MobileBackendFailureKind FailureKind { get; } = failureKind;
}
