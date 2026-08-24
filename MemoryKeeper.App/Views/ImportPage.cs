using MemoryKeeper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Views;

public sealed class ImportPage : Page
{
    public ImportViewModel ViewModel { get; }

    public ImportPage(ImportView view)
    {
        ViewModel = view.ViewModel;
        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition(),
            },
            Children =
            {
                BuildHeader(),
                view,
            }
        };
        Grid.SetRow(view, 1);
    }

    private Grid BuildHeader()
    {
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());

        var back = new Button { Content = "뒤로", Padding = new Microsoft.UI.Xaml.Thickness(10, 4, 10, 4) };
        back.Command = ViewModel.GoBackCommand;
        header.Children.Add(back);

        var title = new TextBlock
        {
            Text = "사진 등록",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        return header;
    }
}
