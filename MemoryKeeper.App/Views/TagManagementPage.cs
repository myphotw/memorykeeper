using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed class TagManagementPage : Page
{
    public TagManagementViewModel ViewModel { get; }

    public TagManagementPage(TagManagementView view)
    {
        ViewModel = view.ViewModel;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new Button
        {
            Content = "뒤로",
            Command = ViewModel.GoBackCommand,
            Padding = new Thickness(10, 4, 10, 4),
        });
        header.Children.Add(new TextBlock
        {
            Text = "태그 관리",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });

        root.Children.Add(header);
        Grid.SetRow(view, 1);
        root.Children.Add(view);
        Content = root;
    }
}
