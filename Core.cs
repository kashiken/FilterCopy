using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FilterCopy;

public record FileEntry(string Source, string Relative);
public record FilterSettings(string Text = "", bool Regex = false, bool MatchCase = false, bool RelativePath = false, bool Wildcard = false, string ExcludeText = "")
{
    [JsonIgnore]
    public string HistoryDescription => $"含める: {(Text.Length == 0 ? "すべて" : Text)} / 除外: {(ExcludeText.Length == 0 ? "なし" : ExcludeText)}";
}
public record ScanResult(List<FileEntry> Files, List<string> Warnings);

public static class FileCatalog
{
    public static ScanResult Scan(IEnumerable<string> sources, CancellationToken token)
    {
        var files = new List<FileEntry>();
        var warnings = new List<string>();
        var roots = sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            if (roots.Any(other => !string.Equals(other, root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(other) && IsWithin(root, other))) continue;
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
            if (name.Length == 0) name = root[..1] + "_Drive";
            if (File.Exists(root))
            {
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) warnings.Add($"リンクは対象外: {root}");
                else files.Add(new(root, name));
            }
            else if (Directory.Exists(root)) Walk(root, name);
            else warnings.Add($"見つかりません: {root}");
        }
        return new(files, warnings);

        void Walk(string directory, string relative)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                { warnings.Add($"リンク先は走査しません: {directory}"); return; }
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    token.ThrowIfCancellationRequested();
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) { warnings.Add($"リンクは対象外: {file}"); continue; }
                    files.Add(new(file, Path.Combine(relative, Path.GetFileName(file))));
                }
                foreach (var child in Directory.EnumerateDirectories(directory)) Walk(child, Path.Combine(relative, Path.GetFileName(child)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warnings.Add($"読み取り不可: {directory} ({ex.Message})"); }
        }
    }

    public static bool IsWithin(string path, string directory) =>
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static List<FileEntry> Filter(IEnumerable<FileEntry> files, FilterSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var include = CreateMatcher(settings.Text, true, "含める条件");
        var exclude = CreateMatcher(settings.ExcludeText, false, "除外条件");
        var result = new List<FileEntry>();
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var text = settings.RelativePath ? file.Relative : Path.GetFileName(file.Source);
            if (include(text) && !exclude(text)) result.Add(file);
        }
        return result;

        Func<string, bool> CreateMatcher(string condition, bool emptyMatches, string label)
        {
            if (condition.Length == 0) return _ => emptyMatches;
            Regex? expression = null;
            if (settings.Regex || settings.Wildcard)
            {
                var pattern = settings.Regex ? condition
                    : @"\A" + Regex.Escape(condition).Replace(@"\*", ".*").Replace(@"\?", ".") + @"\z";
                var options = settings.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                if (!settings.Regex) options |= RegexOptions.NonBacktracking | RegexOptions.Singleline | RegexOptions.CultureInvariant;
                try { expression = new Regex(pattern, options, TimeSpan.FromMilliseconds(200)); }
                catch (ArgumentException ex) { throw new ArgumentException($"{label}: {ex.Message}", ex); }
            }
            return text =>
            {
                try { return expression is not null ? expression.IsMatch(text) : text.Contains(condition, settings.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase); }
                catch (RegexMatchTimeoutException ex) { throw new ArgumentException($"{label}: 正規表現の処理時間が上限を超えました。", ex); }
            };
        }
    }

    public static List<(string Source, string Destination)> Plan(IEnumerable<FileEntry> files, string destination)
    {
        var result = new List<(string, string)>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var target = Path.GetFullPath(Path.Combine(destination, file.Relative));
            if (!IsWithin(target, destination)) throw new IOException("コピー先の範囲外のパスです。");
            if (string.Equals(file.Source, target, StringComparison.OrdinalIgnoreCase)) throw new IOException("コピー元とコピー先が同じファイルです。");
            if (!targets.Add(target)) throw new IOException($"複数のコピー元が同じコピー先になります: {target}");
            result.Add((file.Source, target));
        }
        foreach (var (_, target) in result)
        {
            for (var parent = Path.GetDirectoryName(target); parent is not null; parent = Path.GetDirectoryName(parent))
            {
                if (targets.Contains(parent) || File.Exists(parent)) throw new IOException($"コピー先でファイルとフォルダが衝突します: {parent}");
            }
            if (Directory.Exists(target)) throw new IOException($"ファイルのコピー先にフォルダがあります: {target}");
        }
        return result;
    }
}

public static class HistoryStore
{
    private static readonly string FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilterCopy", "history.json");
    public static List<FilterSettings> Load()
    {
        try { return JsonSerializer.Deserialize<List<FilterSettings>>(File.ReadAllText(FileName)) ?? []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
    public static void Save(FilterSettings settings)
    {
        var history = Load(); history.RemoveAll(x => x == settings); history.Insert(0, settings);
        Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);
        var temp = FileName + "." + Guid.NewGuid() + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(history.Take(30), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FileName, true);
    }
}
