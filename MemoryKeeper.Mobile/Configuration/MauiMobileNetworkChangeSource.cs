using Microsoft.Maui.Networking;

namespace MemoryKeeper.Mobile.Configuration;

public sealed class MauiMobileNetworkChangeSource : IMobileNetworkChangeSource, IDisposable
{
    private bool _disposed;

    public MauiMobileNetworkChangeSource()
    {
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    public event EventHandler? NetworkChanged;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args) =>
        NetworkChanged?.Invoke(this, EventArgs.Empty);
}
