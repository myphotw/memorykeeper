using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoryKeeper.App.Diagnostics;

public sealed partial class ErrorDialogWindow : Window
{
    private readonly string _clipboardText;
    private readonly string _logPath;

    public ErrorDialogWindow(string title, string displayText, string clipboardText, string logPath)
    {
        _clipboardText = clipboardText;
        _logPath = logPath;
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        ErrorTextBox.Text = displayText;

        var appWindow = AppWindow;
        if (appWindow is not null)
        {
            appWindow.Resize(new Windows.Graphics.SizeInt32(720, 560));
        }
    }

    private void CopyAll_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorDialog.CopyToClipboard(_clipboardText);
            StatusText.Text = "클립보드에 복사했습니다.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"복사 실패: {ex.Message}";
            StartupDiagnostics.WriteException("ErrorDialogWindow.CopyAll", ex);
        }
    }

    private void OpenLog_OnClick(object sender, RoutedEventArgs e)
    {
        ErrorDialog.OpenLogLocation(_logPath);
        StatusText.Text = $"로그 위치: {_logPath}";
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
