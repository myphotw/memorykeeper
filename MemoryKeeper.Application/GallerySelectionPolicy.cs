namespace MemoryKeeper.Application;

public static class GallerySelectionPolicy
{
    public const string SelectAllLabel = "전체 선택";
    public const string ClearAllLabel = "전체 해제";

    public static bool AreAllLoadedItemsSelected(int loadedCount, int selectedCount) =>
        loadedCount > 0 && selectedCount == loadedCount;

    public static string GetToggleLabel(int loadedCount, int selectedCount) =>
        AreAllLoadedItemsSelected(loadedCount, selectedCount) ? ClearAllLabel : SelectAllLabel;
}
