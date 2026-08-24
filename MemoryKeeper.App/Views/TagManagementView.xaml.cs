using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed partial class TagManagementView : UserControl
{
    public TagManagementViewModel ViewModel { get; }

    public TagManagementView(TagManagementViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private async void SaveName_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTag is null)
        {
            ViewModel.StatusMessage = "이름을 변경할 태그를 선택하세요.";
            return;
        }

        var target = ViewModel.FindExistingName(ViewModel.Name);
        if (target is not null)
        {
            var source = ViewModel.SelectedTag;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "태그 합치기",
                Content = $"이미 '{target.DisplayName}' 태그가 있습니다.\n"
                          + $"'{source.DisplayName}' 태그를 사용한 {source.UsageCount}장의 사진을 "
                          + $"'{target.DisplayName}' 태그로 정리할까요?",
                PrimaryButtonText = "합치기",
                CloseButtonText = "취소",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        await ViewModel.SaveNameCommand.ExecuteAsync(null);
    }

    private async void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTag is null)
        {
            ViewModel.StatusMessage = "삭제할 태그를 선택하세요.";
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "태그 삭제",
            Content = $"'{ViewModel.SelectedTag.DisplayName}' 태그를 삭제할까요?\n\n"
                      + "태그만 제거되며 사진은 삭제되지 않습니다.",
            PrimaryButtonText = "삭제",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteCommand.ExecuteAsync(null);
        }
    }
}
