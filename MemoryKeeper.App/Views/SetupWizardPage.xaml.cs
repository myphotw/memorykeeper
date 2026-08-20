using MemoryKeeper.App.ViewModels;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed partial class SetupWizardPage : Page
{
    public SetupWizardViewModel ViewModel { get; }

    public SetupWizardPage(SetupWizardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        Loaded += OnLoaded;
        UpdateStepVisibility();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await ViewModel.LoadCommand.ExecuteAsync(null);

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SetupWizardViewModel.CurrentStep)
            or nameof(SetupWizardViewModel.CanFinish))
        {
            UpdateStepVisibility();
        }
    }

    private void UpdateStepVisibility()
    {
        Step1Panel.Visibility = ViewModel.CurrentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = ViewModel.CurrentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = ViewModel.CanFinish ? Visibility.Collapsed : Visibility.Visible;
        FinishButton.Visibility = ViewModel.CanFinish ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HomeSuggestion_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaceSuggestionDto suggestion)
        {
            _ = ViewModel.SelectHomeSuggestionCommand.ExecuteAsync(suggestion);
        }
    }
}
