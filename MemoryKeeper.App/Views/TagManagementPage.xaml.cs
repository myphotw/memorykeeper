using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed partial class TagManagementPage : Page
{
    public TagManagementViewModel ViewModel { get; }

    public TagManagementPage(TagManagementViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private async void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTag is null)
        {
            ViewModel.StatusMessage = "삭제할 Tag를 선택하세요.";
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Tag 삭제",
            Content = $"Tag '{ViewModel.SelectedTag.Name}'을(를) 삭제할까요?\n연결된 MediaTag만 제거되며 사진은 유지됩니다.",
            PrimaryButtonText = "삭제",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteCommand.ExecuteAsync(null);
        }
    }

    private async void Merge_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTag is null || ViewModel.MergeTargetTag is null)
        {
            ViewModel.StatusMessage = "병합할 원본 태그와 대상 태그를 선택하세요.";
            return;
        }

        if (ViewModel.SelectedTag.Id == ViewModel.MergeTargetTag.Id)
        {
            ViewModel.StatusMessage = "서로 다른 태그를 선택하세요.";
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "태그 병합",
            Content = $"'{ViewModel.SelectedTag.Name}'을(를) '{ViewModel.MergeTargetTag.Name}'에 병합할까요?",
            PrimaryButtonText = "병합",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.MergeCommand.ExecuteAsync(null);
        }
    }
}
