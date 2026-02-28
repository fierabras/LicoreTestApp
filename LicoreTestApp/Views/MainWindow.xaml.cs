using System.Diagnostics;
using System.Windows;
using LicoreTestApp.Interop;
using LicoreTestApp.ViewModels;

namespace LicoreTestApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // ── Smoke test: ping + version ────────────────────────────────────────
        int pingResult = LicoreApi.lc_ping();
        string version = LicoreApi.GetVersion();

        Debug.WriteLine($"[LicoreApi] lc_ping()    = {(LicoreApi.LcResult)pingResult}");
        Debug.WriteLine($"[LicoreApi] lc_version() = {version}");

        if (pingResult != (int)LicoreApi.LcResult.Ok)
        {
            MessageBox.Show(
                $"lc_ping() failed: {(LicoreApi.LcResult)pingResult}",
                "Licore initialisation error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }
    }
}
