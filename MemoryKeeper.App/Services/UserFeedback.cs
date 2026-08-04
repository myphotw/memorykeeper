using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Services;

/// <summary>
/// Unified user feedback: InfoBar-style status for normal completion,
/// ContentDialog confirm for destructive actions.
/// </summary>
public static class UserFeedback
{
    public static async Task<bool> ConfirmAsync(
        XamlRoot? xamlRoot,
        string title,
        string message,
        string primaryText = "확인",
        string closeText = "취소")
    {
        if (xamlRoot is null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task ShowInfoAsync(
        XamlRoot? xamlRoot,
        string title,
        string message,
        string closeText = "확인")
    {
        if (xamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        await dialog.ShowAsync();
    }
}
