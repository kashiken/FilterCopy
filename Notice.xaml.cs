using System.Windows;

namespace FilterCopy;

public partial class Notice : Window
{
    public Notice() => InitializeComponent();
    public static void Show(Window? owner, string text, string title)
    {
        var dialog = new Notice { Title = title };
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.Message.Text = text;
        dialog.ShowDialog();
    }
    private void CloseNotice(object sender, RoutedEventArgs e) => Close();
}
