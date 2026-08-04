namespace MemoryKeeper.Application.Layout;

/// <summary>
/// Shared adaptive layout rules for window-width breakpoints.
/// Small &lt; 1200, Medium 1200–1699, Large ≥ 1700.
/// </summary>
public static class ResponsiveLayoutRules
{
    public const double SmallMaxExclusive = 1200;
    public const double MediumMaxExclusive = 1700;

    public static LayoutBreakpoint FromWidth(double width)
    {
        if (width < SmallMaxExclusive)
        {
            return LayoutBreakpoint.Small;
        }

        if (width < MediumMaxExclusive)
        {
            return LayoutBreakpoint.Medium;
        }

        return LayoutBreakpoint.Large;
    }

    public static int FilmStripVisibleRadius(LayoutBreakpoint breakpoint) =>
        breakpoint switch
        {
            LayoutBreakpoint.Small => 5,
            LayoutBreakpoint.Medium => 10,
            _ => 16
        };

    public static double FilmStripMaxWidth(LayoutBreakpoint breakpoint) =>
        breakpoint switch
        {
            LayoutBreakpoint.Small => 420,
            LayoutBreakpoint.Medium => 720,
            _ => 960
        };

    public static double ContentMaxWidth(LayoutBreakpoint breakpoint) =>
        breakpoint switch
        {
            LayoutBreakpoint.Small => 720,
            LayoutBreakpoint.Medium => 1000,
            _ => 1200
        };

    public static double CardPadding(LayoutBreakpoint breakpoint) =>
        breakpoint switch
        {
            LayoutBreakpoint.Small => 12,
            LayoutBreakpoint.Medium => 16,
            _ => 16
        };

    public static double CardSpacing(LayoutBreakpoint breakpoint) =>
        breakpoint switch
        {
            LayoutBreakpoint.Small => 8,
            LayoutBreakpoint.Medium => 10,
            _ => 12
        };

    public static bool UseStackedColumns(LayoutBreakpoint breakpoint) =>
        breakpoint == LayoutBreakpoint.Small;
}
