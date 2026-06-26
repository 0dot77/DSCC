using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DSCC.App.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? singleInstanceMutex;
    private bool ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\DSCC.App.Wpf.SingleInstance", out var createdNew);
        ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsSingleInstanceMutex)
        {
            singleInstanceMutex?.ReleaseMutex();
        }

        singleInstanceMutex?.Dispose();
        singleInstanceMutex = null;
        ownsSingleInstanceMutex = false;
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogUnhandledException("DispatcherUnhandledException", e.Exception);
        MessageBox.Show(
            "DSCC startup failed. See Log\\DSCC.App.crash.log for details.",
            "DSCC",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogUnhandledException("UnhandledException", exception);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogUnhandledException("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void LogUnhandledException(string source, Exception exception)
    {
        try
        {
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "Log");
            Directory.CreateDirectory(logDirectory);

            var builder = new StringBuilder();
            builder.AppendLine($"[{DateTimeOffset.Now:O}] {source}");
            builder.AppendLine(exception.ToString());
            builder.AppendLine();

            File.AppendAllText(Path.Combine(logDirectory, "DSCC.App.crash.log"), builder.ToString());
        }
        catch
        {
            // Avoid recursive crashes while reporting startup failures.
        }
    }
}
