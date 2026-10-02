namespace MemoryKeeper.Mobile.Controls;

public enum MobileViewerNavigationDirection
{
    None,
    Previous,
    Next,
}

public static class MobileViewerGestureDecision
{
    private const double NavigationScaleEpsilon = 0.01;

    public static MobileViewerNavigationDirection ResolveSwipe(
        double scale,
        bool hadMultiplePointers,
        double deltaX,
        double deltaY,
        double velocityX,
        double touchSlop,
        double minimumFlingVelocity)
    {
        if (hadMultiplePointers || scale > MobileZoomState.MinimumScale + NavigationScaleEpsilon)
        {
            return MobileViewerNavigationDirection.None;
        }

        var horizontalDistance = Math.Abs(deltaX);
        var verticalDistance = Math.Abs(deltaY);
        if (horizontalDistance < Math.Max(0, touchSlop) * 2
            || horizontalDistance <= verticalDistance
            || Math.Abs(velocityX) < Math.Max(0, minimumFlingVelocity))
        {
            return MobileViewerNavigationDirection.None;
        }

        return deltaX < 0
            ? MobileViewerNavigationDirection.Next
            : MobileViewerNavigationDirection.Previous;
    }
}
