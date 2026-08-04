using System.Diagnostics;
using System.Reflection;
using MemoryKeeper.Application.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace MemoryKeeper.App.Diagnostics;

/// <summary>
/// Shows a copyable/scrollable error window (replaces MessageBox).
/// </summary>
public static class ErrorDialog
{
    private static readonly object Sync = new();
    private static DispatcherQueue? _uiDispatcher;
    private static bool _isShowing;

    public static void RegisterUiDispatcher(DispatcherQueue dispatcher)
    {
        _uiDispatcher = dispatcher;
    }

    public static void Show(
        ErrorReportSource source,
        string title,
        Exception exception,
        string? stage = null)
    {
        RunOnUi(() => ShowCore(source, title, exception, stage));
    }

    public static void ShowMessage(
        ErrorReportSource source,
        string title,
        string message,
        string? stage = null)
    {
        Show(source, title, new InvalidOperationException(message), stage);
    }

    private static void ShowCore(
        ErrorReportSource source,
        string title,
        Exception exception,
        string? stage)
    {
        lock (Sync)
        {
            if (_isShowing)
            {
                StartupDiagnostics.WriteStep($"ErrorDialog skipped (already showing): {title}");
                return;
            }

            _isShowing = true;
        }

        try
        {
            var time = DateTimeOffset.Now;
            var version = ResolveVersion();
            var display = ErrorReportFormatter.BuildDisplayText(exception, stage);
            var clipboard = ErrorReportFormatter.BuildClipboardText(exception, stage, time, version, source);
            var logPath = ResolveLogPath(source);

            var window = new ErrorDialogWindow(
                title,
                display,
                clipboard,
                logPath);
            window.Closed += (_, _) =>
            {
                lock (Sync)
                {
                    _isShowing = false;
                }
            };
            window.Activate();
        }
        catch (Exception ex)
        {
            lock (Sync)
            {
                _isShowing = false;
            }

            StartupDiagnostics.WriteException("ErrorDialog.ShowCore", ex);
        }
    }

    public static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text ?? string.Empty);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    public static void OpenLogLocation(string logPath)
    {
        try
        {
            Directory.CreateDirectory(StartupDiagnostics.LogDirectory);
            if (File.Exists(logPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{logPath}\"",
                    UseShellExecute = true
                });
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{StartupDiagnostics.LogDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException("ErrorDialog.OpenLogLocation", ex);
        }
    }

    public static string ResolveLogPath(ErrorReportSource source)
    {
        var target = ErrorReportFormatter.GetLogTarget(source);
        return target == ErrorLogTarget.Gallery
            ? GalleryDiagnostics.LogFilePath
            : StartupDiagnostics.LogFilePath;
    }

    public static string ResolveVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static void RunOnUi(Action action)
    {
        var current = DispatcherQueue.GetForCurrentThread();
        if (current is not null && current.HasThreadAccess)
        {
            action();
            return;
        }

        var dispatcher = _uiDispatcher ?? current;
        if (dispatcher is null)
        {
            action();
            return;
        }

        if (!dispatcher.TryEnqueue(() => action()))
        {
            StartupDiagnostics.WriteStep("ErrorDialog: failed to enqueue UI show");
        }
    }
}
