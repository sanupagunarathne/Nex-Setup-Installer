using Microsoft.Win32;
using System.Windows;
using NexInstaller.App.ViewModels;
namespace NexInstaller.App.Services;
public sealed class ImportDialogs : IImportDialogs
{
    public string? Installer()
    {
        var dialog = new OpenFileDialog { Filter = "Offline installers|*.exe;*.msi", CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    public string? Folder()
    {
        var dialog = new OpenFolderDialog { Title = "Select local source folder" };
        return ShowOwned(dialog) ? dialog.FolderName : null;
    }
    public IReadOnlyList<string> Installers()
    {
        var dialog = new OpenFileDialog { Title = "Choose installers for a collection", Filter = "Offline installers|*.exe;*.msi", Multiselect = true, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }
    public string? EntryPoint(string directory)
    {
        var dialog = new OpenFileDialog { Filter = "Installer entry point|*.exe;*.msi", InitialDirectory = directory, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    public IReadOnlyList<string> Fonts()
    {
        var dialog = new OpenFileDialog { Title = "Choose font files", Filter = "Font files|*.ttf;*.otf", Multiselect = true, CheckFileExists = true };
        return ShowOwned(dialog) ? dialog.FileNames : [];
    }
    private static bool ShowOwned(CommonDialog dialog) =>
        (Application.Current?.MainWindow is { IsVisible: true } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;
    public bool Confirm(string message) => MessageBox.Show(message, "STAK Installer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
