using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace FilterCopy;

public sealed class HotkeyHost : IDisposable
{
    private readonly HwndSource source;
    private readonly Forms.NotifyIcon tray;
    private readonly Action<string?> open;
    private readonly Mutex mutex;
    private readonly System.Drawing.Icon icon;
    public HotkeyHost(Action<string?> open)
    {
        this.open = open;
        mutex = new Mutex(true, @"Local\FilterCopy.Hotkey", out var created);
        if (!created) { mutex.Dispose(); throw new InvalidOperationException("FilterCopyは既に常駐しています。"); }
        source = new HwndSource(new HwndSourceParameters("FilterCopy Hotkey") { ParentWindow = new nint(-3) });
        source.AddHook(Hook);
        // Ctrl+Alt+V, no keyboard autorepeat.
        if (!RegisterHotKey(source.Handle, 1, 0x4000 | 0x0002 | 0x0001, 0x56))
        { source.Dispose(); mutex.ReleaseMutex(); mutex.Dispose(); throw new InvalidOperationException("Ctrl+Alt+Vを登録できません。他のアプリが使用している可能性があります。"); }
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => TrayTheme.Apply(menu);
        menu.Items.Add("コピー先を選んで開く", null, (_, _) => open(null));
        menu.Items.Add("バージョン情報", null, (_, _) => AppVersion.Show(System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault()));
        menu.Items.Add("終了", null, (_, _) => System.Windows.Application.Current.Shutdown());
        using var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/FilterCopy;component/Assets/FilterCopy.ico")).Stream;
        using var sourceIcon = new System.Drawing.Icon(iconStream, 32, 32);
        icon = (System.Drawing.Icon)sourceIcon.Clone();
        tray = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = $"FilterCopy {AppVersion.Current} — Ctrl+Alt+V",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => open(null);
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == 1)
        {
            handled = true;
            var destination = ExplorerFolder();
            // Delay opening until COM enumeration has returned and native message handling ends.
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => open(destination));
        }
        return 0;
    }
    private static string? ExplorerFolder()
    {
        object? shell = null, windows = null;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var foreground = GetForegroundWindow();
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            windows = ((dynamic)shell!).Windows();
            foreach (object window in (System.Collections.IEnumerable)windows)
            {
                object? document = null, folder = null, self = null;
                try
                {
                    if (new nint((long)((dynamic)window).HWND) != foreground) continue;
                    document = ((dynamic)window).Document;
                    folder = ((dynamic)document!).Folder;
                    self = ((dynamic)folder!).Self;
                    string path = ((dynamic)self!).Path;
                    if (Directory.Exists(path)) paths.Add(path);
                }
                catch (Exception) { /* Virtual folders and unrelated Shell windows are ignored. */ }
                finally { Release(self); Release(folder); Release(document); Release(window); }
            }
        }
        catch (Exception) { return null; }
        finally { Release(windows); Release(shell); }
        // Shell automation may expose multiple tabs with the same HWND. Never guess a destination.
        return paths.Count == 1 ? paths.Single() : null;
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    public void Dispose()
    {
        tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); icon.Dispose();
        UnregisterHotKey(source.Handle, 1); source.Dispose(); mutex.ReleaseMutex(); mutex.Dispose();
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
