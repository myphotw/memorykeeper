using MemoryKeeper.Application.Layout;

namespace MemoryKeeper.App.Services;

public sealed class LayoutBreakpointChangedEventArgs : EventArgs
{
    public LayoutBreakpointChangedEventArgs(
        LayoutBreakpoint previous,
        LayoutBreakpoint current,
        double windowWidth)
    {
        Previous = previous;
        Current = current;
        WindowWidth = windowWidth;
    }

    public LayoutBreakpoint Previous { get; }
    public LayoutBreakpoint Current { get; }
    public double WindowWidth { get; }
}

public interface IResponsiveLayoutService
{
    LayoutBreakpoint CurrentBreakpoint { get; }

    double WindowWidth { get; }

    /// <summary>Raised when the breakpoint changes.</summary>
    event EventHandler<LayoutBreakpointChangedEventArgs>? BreakpointChanged;

    /// <summary>Raised on any window-width update (including same breakpoint).</summary>
    event EventHandler? LayoutChanged;

    void UpdateWindowWidth(double width);
}
