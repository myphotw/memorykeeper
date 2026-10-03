namespace MemoryKeeper.Mobile.Configuration;

public interface IMobileNetworkChangeSource
{
    event EventHandler? NetworkChanged;
}
