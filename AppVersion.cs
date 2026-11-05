using System.Reflection;
using System.IO;
using System.Windows;

namespace FilterCopy;

public static class AppVersion
{
    public static string Current { get; } = typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0";

    public static string LicenseText
    {
        get
        {
            using var stream = typeof(AppVersion).Assembly.GetManifestResourceStream("FilterCopy.LICENSE")
                ?? throw new InvalidOperationException("ライセンス情報が見つかりません。");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    public static void Show(Window? owner) => Notice.Show(owner,
        $"FilterCopy\nバージョン {Current}\n\nファイルを検索条件で絞り込み、階層を保ってコピーします。\n\nWindows Shell / .NET / WPF\n\nライセンス: MIT\n同梱.NETの権利表記は配布物のlicensesフォルダを参照してください。\n\n{LicenseText}",
        "FilterCopy のバージョン情報");
}
