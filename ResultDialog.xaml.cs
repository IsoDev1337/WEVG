using System.Windows;
using System.Windows.Input;

namespace WEVisualizer;

/// <summary>
/// Small modern themed dialog used instead of the stock Windows MessageBox for
/// success/info results. Returns true when the primary action is chosen.
/// </summary>
public partial class ResultDialog : Window
{
    public ResultDialog() => InitializeComponent();

    private void Primary_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
    private void Secondary_Click(object sender, RoutedEventArgs e) { DialogResult = false; }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
        else if (e.Key == Key.Enter) DialogResult = true;
    }

    /// <summary>
    /// Shows the dialog. If <paramref name="primaryText"/> is null only a single
    /// dismiss button is shown. Returns true if the primary action was chosen.
    /// </summary>
    public static bool Show(Window? owner, string title, string message,
        string? primaryText = null, string secondaryText = "Close", bool success = true)
    {
        var dlg = new ResultDialog { Owner = owner };
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        dlg.SecondaryButton.Content = secondaryText;

        if (primaryText == null)
        {
            dlg.PrimaryButton.Visibility = Visibility.Collapsed;
            dlg.SecondaryButton.Content = secondaryText == "Close" ? "OK" : secondaryText;
        }
        else
        {
            dlg.PrimaryButton.Content = primaryText;
        }

        if (!success)
        {
            dlg.Glyph.Text = "!";
            dlg.Glyph.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));
            ((System.Windows.Controls.Border)dlg.Glyph.Parent).Background =
                new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0x4F, 0x4F));
        }

        // No owner (e.g. very early failure) → center on screen instead.
        if (owner == null) dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dlg.ShowDialog() == true;
    }
}
