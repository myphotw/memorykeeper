using MemoryKeeper.App.Models;
using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MemoryKeeper.App.Views;

public sealed partial class TimelinePage : Page
{
    public TimelineViewModel ViewModel { get; }

    public TimelinePage(TimelineViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.IsSuggestionOpen) && ViewModel.IsSuggestionOpen)
        {
            DispatcherQueue.TryEnqueue(() => BringSuggestionPanelIntoView(SuggestionsPanel));
        }
        else if (e.PropertyName is nameof(ViewModel.IsRecentOpen) && ViewModel.IsRecentOpen)
        {
            DispatcherQueue.TryEnqueue(() => BringSuggestionPanelIntoView(RecentPanel));
        }
    }

    private static void BringSuggestionPanelIntoView(FrameworkElement panel) =>
        panel.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalOffset = 24
        });

    private void PlaceSelectButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TimelinePlaceItem item })
        {
            ViewModel.SelectPlaceCommand.Execute(item);
        }
    }

    private void PlaceOpenPhotoButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TimelinePlaceItem item })
        {
            ViewModel.OpenPhotoDetailCommand.Execute(item);
        }
    }

    private async void SearchBox_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private async void SearchBox_OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.SearchText))
        {
            await ViewModel.ShowRecentCommand.ExecuteAsync(null);
        }
    }

    private async void Suggestion_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchSuggestionItem item)
        {
            await ViewModel.ApplySuggestionCommand.ExecuteAsync(item);
        }
    }

    private async void Recent_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string query)
        {
            await ViewModel.ApplyRecentQueryCommand.ExecuteAsync(query);
        }
    }
}
