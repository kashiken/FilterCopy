using System.IO;
using System.Windows;

namespace FilterCopy;

public partial class App : Application
{
    private HotkeyHost? host;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--resident"))
        {
            try { host = new HotkeyHost(Open); }
            catch (Exception ex) { Notice.Show(null, ex.Message, "FilterCopy"); Shutdown(1); }
            return;
        }
        var index = Array.IndexOf(e.Args, "--destination");
        Open(index >= 0 && index + 1 < e.Args.Length ? e.Args[index + 1] : null);
    }

    private void Open(string? destination)
    {
        if (Windows.OfType<MainWindow>().FirstOrDefault() is { } existing)
        {
            existing.Activate();
            return;
        }
        if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
        {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "コピー先フォルダを選択" };
            if (picker.ShowDialog() != true) { if (host is null) Shutdown(); return; }
            destination = picker.FolderName;
        }
        var window = new MainWindow(destination);
        window.Closed += (_, _) => { if (host is null) Shutdown(); };
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e) { host?.Dispose(); base.OnExit(e); }
}
