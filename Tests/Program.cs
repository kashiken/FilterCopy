using System.IO;
using System.Text.RegularExpressions;
using FilterCopy;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    private static int passed;
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        if (args.Contains("--preview")) { Preview(); return; }
        Run().GetAwaiter().GetResult();
    }

    private static void Preview()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "previews"));
        Directory.CreateDirectory(directory);
        foreach (var dark in new[] { false, true })
        {
            var window = new MainWindow(@"D:\Backup");
#pragma warning disable WPF0001 // Exercise both Fluent palettes without changing Windows settings.
            window.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            ((ComboBox)window.FindName("Search")).Text = "*.dll";
            ((RadioButton)window.FindName("WildcardToggle")).IsChecked = true;
            var root = new TreeNode("Project", "Project");
            var images = new TreeNode("images", @"Project\images");
            images.Children.Add(new("core.dll", @"Project\images\core.dll"));
            images.Children.Add(new("plugin.dll", @"Project\images\plugin.dll"));
            root.Children.Add(images);
            ((TreeView)window.FindName("FilesTree")).ItemsSource = new[] { root };
            ((TextBlock)window.FindName("Status")).Text = "対象: 2 / 128 ファイル";
            ((TextBlock)window.FindName("ErrorLabel")).Text = "検索条件のエラー表示サンプル";
            ((Button)window.FindName("CopyButton")).IsEnabled = true;
            // Attach to a presentation source so Fluent templates and inherited colors resolve.
            // Suppress source scanning in this visual fixture; never read or modify the clipboard.
            var loaded = (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), window, "LoadFiles");
            window.Loaded -= loaded;
            window.ShowActivated = false; window.Opacity = 0;
            window.Left = -10000; window.Top = -10000;
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var tree = (TreeView)window.FindName("FilesTree");
            var rootItem = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
            var nestedItem = (TreeViewItem)rootItem.ItemContainerGenerator.ContainerFromIndex(0);
            Check(rootItem.IsExpanded && nestedItem.IsExpanded && nestedItem.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem, "ネストしたフォルダまで初期状態で展開");
            nestedItem.IsExpanded = false;
            Check(!nestedItem.IsExpanded, "展開後は手動で折りたためる");
            nestedItem.IsExpanded = true;
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(820, 610));
            content.Arrange(new Rect(0, 0, 820, 610));
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(820, 610, 96, 96, PixelFormats.Pbgra32);
            // Window content is rendered without showing the window or touching the clipboard.
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(window.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? window.Background, null, new Rect(0, 0, 820, 610));
                drawing.DrawRectangle(new VisualBrush(content), null, new Rect(18, 18, 784, 574));
            }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var name = Path.Combine(directory, dark ? "dark.png" : "light.png");
            using (var stream = File.Create(name)) encoder.Save(stream);
            Console.WriteLine(name);
            window.Close();
        }
    }
    private static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "FilterCopy-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "Project");
            var nested = Path.Combine(source, "images", "deep");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(source, "README.txt"), "readme");
            File.WriteAllText(Path.Combine(nested, "PHOTO.PNG"), "image payload");
            File.WriteAllText(Path.Combine(nested, "other.jpg"), "other payload");
            var scan = FileCatalog.Scan([source, nested, Path.Combine(source, "README.txt")], default);
            Check(scan.Files.Count == 3 && scan.Warnings.Count == 0, "重複した選択を除外して再帰走査");
            Check(scan.Files.Any(x => x.Relative == Path.Combine("Project", "images", "deep", "PHOTO.PNG")), "選択フォルダの階層を保持");
            Check(FileCatalog.Filter(scan.Files, new("png"), default).Count == 1, "通常検索は大文字小文字を無視");
            Check(FileCatalog.Filter(scan.Files, new("png", MatchCase: true), default).Count == 0, "大文字小文字の区別");
            Check(FileCatalog.Filter(scan.Files, new(@"\.(png|jpg)$", Regex: true), default).Count == 2, "正規表現");
            Check(FileCatalog.Filter(scan.Files, new("images", RelativePath: true), default).Count == 2, "相対パス検索");
            Check(FileCatalog.Filter(scan.Files, new(), default).Count == 3, "空の条件は全件");
            var wildcardFiles = new[] { new FileEntry(@"C:\a.dll", "a.dll"), new FileEntry(@"C:\AB.DLL", "AB.DLL"), new FileEntry(@"C:\a.dll.bak", "a.dll.bak"), new FileEntry(@"C:\a[1].dll", @"plugins\a[1].dll"), new FileEntry(@"C:\README", "README") };
            Check(FileCatalog.Filter(wildcardFiles, new("*.dll", Wildcard: true), default).Count == 3, "ワイルドカードの*と拡張子の末尾一致");
            Check(FileCatalog.Filter(wildcardFiles, new("?.dll", Wildcard: true), default).Count == 1, "ワイルドカードの?は1文字");
            Check(FileCatalog.Filter(wildcardFiles, new("*.dll", Wildcard: true, MatchCase: true), default).Count == 2, "ワイルドカードの大文字小文字区別");
            Check(FileCatalog.Filter(wildcardFiles, new("a[1].dll", Wildcard: true), default).Count == 1, "ワイルドカードでは正規表現の記号を文字として扱う");
            Check(FileCatalog.Filter(wildcardFiles, new("*", Wildcard: true), default).Count == 5, "*だけで拡張子のないファイルも全件一致");
            Check(FileCatalog.Filter(wildcardFiles, new(@"plugins\*.dll", Wildcard: true, RelativePath: true), default).Count == 1, "相対パスのワイルドカード");
            Check(FileCatalog.Filter(wildcardFiles, new("", Wildcard: true), default).Count == 5, "ワイルドカードでも空欄は全件");
            Check(System.Text.Json.JsonSerializer.Deserialize<FilterSettings>("{\"Text\":\"dll\",\"Regex\":false}") is { Wildcard: false }, "以前の履歴との互換性");
            var wildcardHistory = new FilterSettings("*.dll", Wildcard: true, MatchCase: true);
            Check(System.Text.Json.JsonSerializer.Deserialize<FilterSettings>(System.Text.Json.JsonSerializer.Serialize(wildcardHistory)) == wildcardHistory, "履歴にワイルドカードとオプションを保持");
            Throws<ArgumentException>(() => FileCatalog.Filter(scan.Files, new("[", Regex: true), default), "不正な正規表現を検出");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Throws<OperationCanceledException>(() => FileCatalog.Scan([source], canceled.Token), "走査キャンセル");
            var destination = Path.Combine(root, "destination"); Directory.CreateDirectory(destination);
            var window = new MainWindow(destination);
            var appAssembly = typeof(AppVersion).Assembly;
            Check(System.Text.RegularExpressions.Regex.IsMatch(AppVersion.Current, @"\A1\.\d+\z") && appAssembly.GetName().Version?.Major == 1, "メジャー1のバージョン情報をアセンブリから取得");
            Check(window.Title.Contains(AppVersion.Current, StringComparison.Ordinal), "タイトルに現在のバージョンを表示");
            Check(window.FindName("FilesTree") is System.Windows.Controls.TreeView && window.FindName("CopyButton") is System.Windows.Controls.Button { IsEnabled: false }, "WPF画面の生成と初期状態");
            Check(window.Icon is BitmapFrame, "ウィンドウの独自アイコンを読み込み");
            var errorLabel = (TextBlock)window.FindName("ErrorLabel");
#pragma warning disable WPF0001 // Validate theme resources rather than only the ThemeMode setting.
            window.ThemeMode = ThemeMode.Dark;
            Check(window.TryFindResource("SystemFillColorCriticalBrush") is SolidColorBrush, "ダークテーマのエラー色リソース");
            var darkError = ((SolidColorBrush)errorLabel.Foreground).Color;
            window.ThemeMode = ThemeMode.Light;
            Check(((SolidColorBrush)errorLabel.Foreground).Color != darkError, "テーマ切替でエラー文字色も追従");
#pragma warning restore WPF0001
            var notice = new Notice();
            Check(notice.Icon is BitmapFrame, "詳細ダイアログのアイコンを読み込み");
            notice.Close();
            window.Close();
            Throws<IOException>(() => FileCatalog.Plan([new("a", "..\\escape")], destination), "コピー先の範囲外を拒否");
            Throws<IOException>(() => FileCatalog.Plan([new("a", "same"), new("b", "SAME")], destination), "コピー先の衝突を拒否");
            Throws<IOException>(() => FileCatalog.Plan([new("a", "same"), new("b", "same\\child")], destination), "ファイルとフォルダになるパスの衝突を拒否");
            Throws<IOException>(() => FileCatalog.Plan([new(Path.Combine(destination, "same"), "same")], destination), "同一ファイルへのコピーを拒否");
            Check(!FileCatalog.IsWithin(destination + "-other", destination), "類似名の兄弟フォルダは配下と判定しない");
            var selected = FileCatalog.Filter(scan.Files, new(@"\.(png|jpg)$", Regex: true), default);
            var outcome = await ShellCopy.RunAsync(FileCatalog.Plan(selected, destination), 0);
            Check(!outcome.Aborted && outcome.Errors.Count == 0, "IFileOperationで異なるファイルを一括コピー");
            Check(File.ReadAllText(Path.Combine(destination, "Project", "images", "deep", "PHOTO.PNG")) == "image payload", "Shellコピー後の階層と内容");
            Check(!File.Exists(Path.Combine(destination, "Project", "README.txt")), "除外ファイルはコピーしない");
            Check(File.Exists(Path.Combine(source, "README.txt")), "コピー元は保持");
            var many = Path.Combine(source, "many"); Directory.CreateDirectory(many);
            for (var i = 0; i < 100; i++) File.WriteAllText(Path.Combine(many, $"file-{i:D3}.txt"), i.ToString());
            var batch = FileCatalog.Scan([many], default);
            var batchResult = await ShellCopy.RunAsync(FileCatalog.Plan(batch.Files, destination), 0);
            Check(!batchResult.Aborted && batchResult.Errors.Count == 0 && Directory.GetFiles(Path.Combine(destination, "many")).Length == 100, "100ファイルのShell一括コピー");
            Console.WriteLine($"PASS: {passed} checks");
        }
        finally
        {
            // Delete only the unique fixture directory allocated by this test.
            if (Path.GetFileName(root).StartsWith("FilterCopy-tests-", StringComparison.Ordinal) && Path.GetDirectoryName(root) == Path.TrimEndingDirectorySeparator(Path.GetTempPath()))
                Directory.Delete(root, true);
        }
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    private static void Throws<T>(Action action, string name) where T : Exception
    { try { action(); } catch (T) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
}
