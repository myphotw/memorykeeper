using MemoryKeeper.Application.Layout;

namespace MemoryKeeper.App.Services;

public sealed class ResponsiveLayoutService : IResponsiveLayoutService
{
    private LayoutBreakpoint _breakpoint = LayoutBreakpoint.Medium;
    private double _windowWidth = 1280;

    public LayoutBreakpoint CurrentBreakpoint => _breakpoint;

    public double WindowWidth => _windowWidth;

    public event EventHandler<LayoutBreakpointChangedEventArgs>? BreakpointChanged;

    public event EventHandler? LayoutChanged;

    public void UpdateWindowWidth(double width)
    {
        if (double.IsNaN(width) || width <= 0)
        {
            return;
        }

        _windowWidth = width;
        var next = ResponsiveLayoutRules.FromWidth(width);
        if (next != _breakpoint)
        {
            var previous = _breakpoint;
            _breakpoint = next;
            BreakpointChanged?.Invoke(
                this,
                new LayoutBreakpointChangedEventArgs(previous, next, width));
        }

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
