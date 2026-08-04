using MemoryKeeper.App.ViewModels;
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
        UpdateStepVisibility();
    }

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
        Step3Panel.Visibility = ViewModel.CurrentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = ViewModel.CurrentStep == 4 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = ViewModel.CanFinish ? Visibility.Collapsed : Visibility.Visible;
        FinishButton.Visibility = ViewModel.CanFinish ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.GoogleMapsApiKey = ApiKeyBox.Password;
    }
}
