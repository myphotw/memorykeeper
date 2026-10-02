namespace MemoryKeeper.Mobile.Controls;

public sealed class MobileViewerPosition
{
    public int CurrentIndex { get; private set; } = -1;

    public bool TryOpen(int index, int itemCount)
    {
        if (index < 0 || index >= itemCount)
        {
            return false;
        }

        CurrentIndex = index;
        return true;
    }

    public bool TryMove(int offset, int itemCount, out int targetIndex)
    {
        targetIndex = CurrentIndex + offset;
        if (CurrentIndex < 0 || targetIndex < 0 || targetIndex >= itemCount)
        {
            targetIndex = CurrentIndex;
            return false;
        }

        CurrentIndex = targetIndex;
        return true;
    }

    public void Clear() => CurrentIndex = -1;
}
