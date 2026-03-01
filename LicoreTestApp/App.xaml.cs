using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace LicoreTestApp;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            string msg = e.Exception is SEHException or AccessViolationException
                ? $"Error crítico de DLL: {e.Exception.GetType().Name}\n{e.Exception.Message}"
                : $"Error inesperado: {e.Exception.Message}";

            MessageBox.Show(msg, "LicoreTestApp — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            TryWriteCrashLog(e.Exception);
            e.Handled = true;   // no tumbar la app
        };
    }

    private static void TryWriteCrashLog(Exception ex)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "licore_testapp_crash.log");
            File.AppendAllText(path,
                $"[{DateTime.UtcNow:u}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n---\n");
        }
        catch { /* silencioso */ }
    }
}
