using Avalonia;
using System;
using System.IO;

namespace SQLRestoreSOS.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Last-resort safety net: an unhandled exception anywhere (a background thread, a
        // fire-and-forget async command, a binding callback) would otherwise silently kill the
        // whole app with no indication why - during a disaster-recovery incident that's exactly
        // the wrong failure mode. This can't prevent the crash (the CLR is already terminating
        // by the time this fires), but it writes what happened to a crash log next to the normal
        // audit log so it can be diagnosed afterward instead of just "the app vanished".
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            throw;
        }
    }

    private static void LogCrash(Exception? ex)
    {
        try
        {
            var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logsDir);
            File.AppendAllText(
                Path.Combine(logsDir, "crash.log"),
                $"{DateTime.UtcNow:O}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging the crash must never itself throw during process teardown.
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
