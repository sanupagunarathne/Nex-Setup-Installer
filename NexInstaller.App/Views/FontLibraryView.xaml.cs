using System.Windows.Controls;
using System.Windows.Media;
using NexInstaller.Core.Importing;
using NexInstaller.Infrastructure.Importing;
namespace NexInstaller.App.Views;
public partial class FontLibraryView : UserControl
{
    public FontLibraryView() => InitializeComponent();
    private void PreviewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewText is null || PreviewNotice is null) return;
        PreviewText.FontFamily = new FontFamily("Segoe UI");
        if (((ListBox)sender).SelectedItem is not FontRecord font) return;
        try
        {
            LibraryStorage.CheckPath(font.Path);
            var typeface = new GlyphTypeface(new Uri(font.Path, UriKind.Absolute));
            var family = typeface.FamilyNames.Values.FirstOrDefault() ?? throw new InvalidOperationException("No font family name.");
            var directory = System.IO.Path.GetDirectoryName(font.Path)! + System.IO.Path.DirectorySeparatorChar;
            PreviewText.FontFamily = new FontFamily(new Uri(directory, UriKind.Absolute), "./#" + family);
            PreviewNotice.Text = "Selected font family: " + family;
        }
        catch (Exception) { PreviewNotice.Text = "Preview unavailable. The file remains in the library; default typeface shown."; }
    }
}
