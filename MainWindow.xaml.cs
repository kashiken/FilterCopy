using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace FilterCopy;

public record TreeNode(string Name, string Path)
{
    public List<TreeNode> Children { get; } = [];
    public Dictionary<string, TreeNode> Index { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public partial class MainWindow : Window
{
    private readonly string destination;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? filtering;
    private List<FileEntry> files = [];
    private List<FileEntry> matches = [];
    private List<string> warnings = [];
    private bool ready, busy, restoring;
    private FilterSettings? lastValidSettings;
    private FilterSettings Settings => new(Search.Text, RegexToggle.IsChecked == true, CaseToggle.IsChecked == true, PathToggle.IsChecked == true, WildcardToggle.IsChecked == true, Exclude.Text);

    public MainWindow(string destination)
    {
        InitializeComponent();
        Title = $"FilterCopy {AppVersion.Current} — フィルタして貼り付け";
        this.destination = Path.GetFullPath(destination);
        DestinationLabel.Text = "コピー先: " + this.destination;
        var history = HistoryStore.Load();
        Search.ItemsSource = Exclude.ItemsSource = history;
        Loaded += LoadFiles;
    }

    private async void LoadFiles(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Clipboard.ContainsFileDropList()) throw new IOException("コピー元がありません。エクスプローラーでファイルやフォルダを選び、Ctrl+Cを押してから起動してください。");
            var sources = Clipboard.GetFileDropList().Cast<string>().ToArray();
            if (sources.Any(source => Directory.Exists(source) && (string.Equals(Path.GetFullPath(source), destination, StringComparison.OrdinalIgnoreCase) || FileCatalog.IsWithin(destination, source))))
                throw new IOException("コピー元フォルダの内部にはコピーできません。別のコピー先を選択してください。");
            var result = await Task.Run(() => FileCatalog.Scan(sources, lifetime.Token));
            if (lifetime.IsCancellationRequested) return;
            files = result.Files; warnings = result.Warnings;
            WarningsButton.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ready = true;
            Search.Focus();
            ApplyFilter();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) { ErrorLabel.Text = ex.Message; Status.Text = "走査できませんでした"; } }
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (ready && !restoring && !busy) ApplyFilter();
    }

    private void HistorySelected(object sender, SelectionChangedEventArgs e)
    {
        if (restoring || (sender as ComboBox)?.SelectedItem is not FilterSettings settings) return;
        restoring = true;
        if (settings.Regex) RegexToggle.IsChecked = true;
        else if (settings.Wildcard) WildcardToggle.IsChecked = true;
        else NormalToggle.IsChecked = true;
        CaseToggle.IsChecked = settings.MatchCase; PathToggle.IsChecked = settings.RelativePath;
        Search.Text = settings.Text;
        Exclude.Text = settings.ExcludeText;
        restoring = false;
        if (ready) ApplyFilter();
    }

    private async void ApplyFilter()
    {
        filtering?.Cancel();
        var current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        filtering = current;
        var settings = Settings;
        CopyButton.IsEnabled = false; ErrorLabel.Text = ""; Status.Text = "絞り込み中…";
        try
        {
            await Task.Delay(220, current.Token);
            var result = await Task.Run(() =>
            {
                var selected = FileCatalog.Filter(files, settings, current.Token);
                var root = new TreeNode("", "");
                foreach (var file in selected)
                {
                    current.Token.ThrowIfCancellationRequested();
                    var node = root;
                    foreach (var part in file.Relative.Split(Path.DirectorySeparatorChar))
                    {
                        if (!node.Index.TryGetValue(part, out var child))
                        { child = new(part, Path.Combine(node.Path, part)); node.Index.Add(part, child); node.Children.Add(child); }
                        node = child;
                    }
                }
                return (selected, root.Children);
            }, current.Token);
            if (current.IsCancellationRequested) return;
            matches = result.selected; FilesTree.ItemsSource = result.Children;
            lastValidSettings = settings;
            Status.Text = $"対象: {matches.Count:N0} / {files.Count:N0} ファイル" + (warnings.Count > 0 ? $"（警告 {warnings.Count} 件）" : "");
            CopyButton.IsEnabled = matches.Count > 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!current.IsCancellationRequested)
            { matches = []; FilesTree.ItemsSource = null; ErrorLabel.Text = "検索条件エラー: " + ex.Message; Status.Text = $"対象: — / {files.Count:N0} ファイル"; }
        }
        finally { if (ReferenceEquals(filtering, current)) filtering = null; current.Dispose(); }
    }

    private async void Copy(object sender, RoutedEventArgs e)
    {
        if (busy || matches.Count == 0 || !CopyButton.IsEnabled) return;
        busy = true; SetEditing(false); Status.Text = "コピーを準備しています…";
        try
        {
            var plan = FileCatalog.Plan(matches, destination);
            try { HistoryStore.Save(Settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorLabel.Text = "履歴を保存できませんでした: " + ex.Message; }
            var owner = new WindowInteropHelper(this).Handle;
            var outcome = await ShellCopy.RunAsync(plan, owner);
            Status.Text = outcome.Aborted ? "コピーは中断されました。一部はコピー済みの場合があります。" : "コピー処理が終了しました。";
            if (outcome.Errors.Count > 0)
            {
                ErrorLabel.Text = $"コピーできなかった項目: {outcome.Errors.Count} 件";
                Notice.Show(this, string.Join(Environment.NewLine, outcome.Errors.Take(20)), "コピー結果");
            }
        }
        catch (Exception ex) { ErrorLabel.Text = ex.Message; Status.Text = "コピーに失敗しました。一部はコピー済みの場合があります。"; }
        finally { busy = false; SetEditing(true); }
    }
    private void SetEditing(bool enabled)
    {
        Search.IsEnabled = Exclude.IsEnabled = NormalToggle.IsEnabled = WildcardToggle.IsEnabled = RegexToggle.IsEnabled = CaseToggle.IsEnabled = PathToggle.IsEnabled = CancelButton.IsEnabled = enabled;
        CopyButton.IsEnabled = enabled && matches.Count > 0;
    }
    private void ShowWarnings(object sender, RoutedEventArgs e) => Notice.Show(this, string.Join(Environment.NewLine, warnings.Take(100)), "走査の詳細（先頭100件）");
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void ShowAbout(object sender, RoutedEventArgs e) => AppVersion.Show(this);
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; return; }
        lifetime.Cancel(); filtering?.Cancel();
        if (lastValidSettings is { } valid && (valid.Text.Length > 0 || valid.ExcludeText.Length > 0))
        {
            try { HistoryStore.Save(lastValidSettings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Closing remains possible if settings are read-only. */ }
        }
    }
}
