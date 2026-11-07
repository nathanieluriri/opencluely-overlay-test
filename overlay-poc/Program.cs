using System.Windows.Forms;

namespace OverlayPoc;

internal static class Program
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverlayPoc");

    internal static string LogPath { get; } = Path.Combine(LogDirectory, "poc.log");

    [STAThread]
    private static void Main()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            Log("START (process alive)");

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => Fatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal(e.ExceptionObject as Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Log("Creating form");
            Application.Run(new OverlayForm());
            Log("EXIT normal");
        }
        catch (Exception ex)
        {
            Fatal(ex);
        }
    }

    internal static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    private static void Fatal(Exception ex)
    {
        var text = ex?.ToString() ?? "Unknown fatal error";
        Log("FATAL: " + text);
        // No MessageBox — the app is already crashing, and a dialog
        // would steal focus from whatever the user is doing.
    }
}
