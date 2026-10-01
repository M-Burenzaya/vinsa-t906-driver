using System.Windows;
namespace VinsaStudio;
public partial class App : Application
{
    internal static string? InitialSettingsPath { get; private set; }
    internal static bool SmokeTest { get; private set; }
    internal static string? PreviewDirectory { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--smoke-test") SmokeTest = true;
            else if (e.Args[i] == "--preview-dir" && i + 1 < e.Args.Length) PreviewDirectory = System.IO.Path.GetFullPath(e.Args[++i]);
            else if (e.Args[i] == "--settings" && i + 1 < e.Args.Length) InitialSettingsPath = e.Args[++i];
            else
            {
                MessageBox.Show("Usage: VinsaStudio.exe [--settings <settings.ini>] [--smoke-test] [--preview-dir <folder>]", "Vinsa Studio");
                Shutdown(2); return;
            }
        }
        if (PreviewDirectory is not null && !SmokeTest) { Shutdown(2); return; }
        new MainWindow().Show();
    }
}
