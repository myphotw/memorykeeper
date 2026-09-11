using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MemoryKeeper.App.Dialogs;

public static class CaptureDateDialog
{
    public static async Task<DateOnly?> ShowChangeAsync(
        XamlRoot xamlRoot,
        int selectedCount,
        ImageSource? representativeImage,
        string currentStatus,
        DateOnly? initialDate = null)
    {
        var picker = new DatePicker
        {
            Date = new DateTimeOffset(
                (initialDate ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue)),
            DayVisible = true,
            MonthVisible = true,
            YearVisible = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var content = new StackPanel { Spacing = 12, MinWidth = 320 };
        if (representativeImage is not null)
        {
            content.Children.Add(new Border
            {
                Width = 112,
                Height = 84,
                CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new Image { Source = representativeImage, Stretch = Stretch.Uniform },
            });
        }

        content.Children.Add(new TextBlock
        {
            Text = $"{selectedCount:N0}장 선택",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        content.Children.Add(new TextBlock
        {
            Text = currentStatus,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });
        content.Children.Add(picker);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "촬영일 변경",
            Content = content,
            PrimaryButtonText = "촬영일 변경",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return DateOnly.FromDateTime(picker.Date.DateTime);
    }

    public static async Task<bool> ConfirmClearAsync(XamlRoot xamlRoot, int selectedCount)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "촬영일 보정 해제",
            Content = $"선택한 {selectedCount:N0}장의 사용자가 지정한 촬영일을 제거하고 원래 사진 정보의 날짜를 사용합니다.",
            PrimaryButtonText = "보정 해제",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
