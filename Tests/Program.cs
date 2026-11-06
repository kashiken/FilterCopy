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
            ((ComboBox)window.FindName("Exclude")).Text = "*Tests*";
            ((RadioButton)window.FindName("WildcardToggle")).IsChecked = true;
            var root = new TreeNode("Project", "Project");
            var images = new TreeNode("images", @"Project\images");
            images.Children.Add(new("core.dll", @"Project\images\core.dll"));
            images.Children.Add(new("plugin.dll", @"Project\images\plugin.dll"));
            root.Children.Add(images);
            ((TreeView)window.FindName("FilesTree")).ItemsSource = new[] { root };
            ((TextBlock)window.FindName("Status")).Text = "対象: 2 / 128 ファイル";
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
            content.Measure(new Size(820, 700));
            content.Arrange(new Rect(0, 0, 820, 700));
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(820, 700, 96, 96, PixelFormats.Pbgra32);
            // Window content is rendered without showing the window or touching the clipboard.
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(window.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? window.Background, null, new Rect(0, 0, 820, 700));
                drawing.DrawRectangle(new VisualBrush(content), null, new Rect(18, 18, 784, 664));
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
            Check(FileCatalog.Filter(wildcardFiles, new(".dll", ExcludeText: "AB"), default).Count == 3, "通常検索の一致結果から除外");
            Check(FileCatalog.Filter(wildcardFiles, new("*.dll", Wildcard: true, ExcludeText: "a.dll"), default).Count == 2, "ワイルドカードの一致結果から除外");
            Check(FileCatalog.Filter(wildcardFiles, new(@"\.dll$", Regex: true, ExcludeText: @"^(AB|a\[1\])"), default).Single().Relative == "a.dll", "正規表現の一致結果から除外");
            Check(FileCatalog.Filter(wildcardFiles, new(Wildcard: true, ExcludeText: "*.dll"), default).Count == 2, "含める条件が空なら全件から除外");
            Check(FileCatalog.Filter(wildcardFiles, new("*.dll", Wildcard: true, ExcludeText: "*"), default).Count == 0, "除外条件が全件に一致したら0件");
            Check(FileCatalog.Filter(wildcardFiles, new("*.dll", Wildcard: true, MatchCase: true, ExcludeText: "A.DLL"), default).Any(x => x.Relative == "a.dll"), "除外にも大文字小文字の区別を適用");
            Check(!FileCatalog.Filter(wildcardFiles, new("*.dll", Wildcard: true, ExcludeText: "A.DLL"), default).Any(x => x.Relative == "a.dll"), "除外も既定で大文字小文字を無視");
            Check(FileCatalog.Filter(wildcardFiles, new("*", Wildcard: true, RelativePath: true, ExcludeText: @"plugins\*"), default).Count == 4, "相対パスに対する除外");
            Check(FileCatalog.Filter(wildcardFiles, new("*", Wildcard: true, ExcludeText: @"plugins\*"), default).Count == 5, "ファイル名検索では除外に親パスを使わない");
            Throws<ArgumentException>(() => FileCatalog.Filter(wildcardFiles, new("no-match", Regex: true, ExcludeText: "["), default), "含める結果が0件でも不正な除外条件を検出");
            Check(System.Text.Json.JsonSerializer.Deserialize<FilterSettings>("{\"Text\":\"*.dll\",\"Wildcard\":true}") is { ExcludeText: "" }, "以前の履歴は除外なしとして復元");
            var pair = new FilterSettings("*.dll", Wildcard: true, MatchCase: true, ExcludeText: "*Tests*");
            var pairJson = System.Text.Json.JsonSerializer.Serialize(pair);
            Check(System.Text.Json.JsonSerializer.Deserialize<FilterSettings>(pairJson) == pair && !pairJson.Contains("HistoryDescription", StringComparison.Ordinal), "包含・除外条件を履歴に保持し表示ラベルは保存しない");
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
            TestFilterWindow(window);
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
            var selected = FileCatalog.Filter(scan.Files, new(@"\.(png|jpg)$", Regex: true, ExcludeText: @"\.jpg$"), default);
            var outcome = await ShellCopy.RunAsync(FileCatalog.Plan(selected, destination), 0);
            Check(!outcome.Aborted && outcome.Errors.Count == 0, "IFileOperationで異なるファイルを一括コピー");
            Check(File.ReadAllText(Path.Combine(destination, "Project", "images", "deep", "PHOTO.PNG")) == "image payload", "Shellコピー後の階層と内容");
            Check(!File.Exists(Path.Combine(destination, "Project", "README.txt")), "除外ファイルはコピーしない");
            Check(!File.Exists(Path.Combine(destination, "Project", "images", "deep", "other.jpg")), "含める条件に一致しても除外したファイルは実コピーしない");
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
    private static void TestFilterWindow(MainWindow window)
    {
        var previousContext = SynchronizationContext.Current;
        var loaded = (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), window, "LoadFiles");
        window.Loaded -= loaded;
        window.ShowActivated = false; window.Opacity = 0;
        window.Left = -10000; window.Top = -10000;
        window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var search = (ComboBox)window.FindName("Search");
        var exclude = (ComboBox)window.FindName("Exclude");
        var wildcard = (RadioButton)window.FindName("WildcardToggle");
        var regex = (RadioButton)window.FindName("RegexToggle");
        var matchCase = (CheckBox)window.FindName("CaseToggle");
        var relative = (CheckBox)window.FindName("PathToggle");
        var copy = (Button)window.FindName("CopyButton");
        var status = (TextBlock)window.FindName("Status");
        var error = (TextBlock)window.FindName("ErrorLabel");
        var history = new FilterSettings("*.dll", Wildcard: true, MatchCase: true, ExcludeText: "*Tests*");
        search.ItemsSource = exclude.ItemsSource = new[] { history, new FilterSettings(ExcludeText: ".pdb") };
        exclude.SelectedIndex = 0;
        Check(search.Text == "*.dll" && exclude.Text == "*Tests*" && wildcard.IsChecked == true && matchCase.IsChecked == true, "除外欄の履歴から両方の条件とオプションを復元");
        search.SelectedIndex = 1;
        Check(search.Text == "" && exclude.Text == ".pdb" && ((RadioButton)window.FindName("NormalToggle")).IsChecked == true, "除外条件だけの履歴を含める欄から復元");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            search.ItemsSource = exclude.ItemsSource = Array.Empty<FilterSettings>();
            matchCase.IsChecked = false; relative.IsChecked = false; wildcard.IsChecked = true;
            search.Text = "*.dll"; exclude.Text = "";
            Set("files", new List<FileEntry>
            {
                new(@"C:\Project\lib\core.dll", @"Project\lib\core.dll"),
                new(@"C:\Project\tests\widget.Tests.dll", @"Project\tests\widget.Tests.dll"),
                new(@"C:\Project\README.txt", @"Project\README.txt")
            });
            Set("ready", true);
            exclude.Text = "*Tests*";
            WaitFor(() => copy.IsEnabled && status.Text == "対象: 1 / 3 ファイル");
            var tree = (TreeView)window.FindName("FilesTree");
            var root = ((IEnumerable<TreeNode>)tree.ItemsSource).Single();
            Check(root.Children.Single().Name == "lib", "除外後の件数とツリーから不要な親フォルダも取り除く");
            search.Text = ""; exclude.Text = "*.txt";
            WaitFor(() => copy.IsEnabled && status.Text == "対象: 2 / 3 ファイル");
            Check(true, "除外条件だけでも画面を更新");
            exclude.Text = "";
            WaitFor(() => copy.IsEnabled && status.Text == "対象: 3 / 3 ファイル");
            Check(true, "除外欄を空に戻すと全候補を復元");
            regex.IsChecked = true; exclude.Text = "[";
            WaitFor(() => error.Text.Contains("除外条件", StringComparison.Ordinal));
            Check(!copy.IsEnabled && tree.ItemsSource is null, "不正な除外正規表現ではコピーを禁止し古い候補を消す");
            exclude.Text = @"\.Tests\.dll$"; search.Text = @"\.dll$";
            WaitFor(() => copy.IsEnabled && status.Text == "対象: 1 / 3 ファイル");
            Check(error.Text.Length == 0, "除外条件を修正すると検索とコピーを再び有効化");
        }
        finally
        {
            Set("ready", false);
            // Never persist synthetic conditions into the user's real history.
            Set("lastValidSettings", null);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
    private static void WaitFor(Func<bool> condition)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if (!condition()) throw new Exception("Timed out waiting for filter UI update.");
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    private static void Throws<T>(Action action, string name) where T : Exception
    { try { action(); } catch (T) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
}
