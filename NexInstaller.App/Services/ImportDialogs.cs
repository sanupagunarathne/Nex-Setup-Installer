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
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
    public string? EntryPoint(string directory)
    {
        var dialog = new OpenFileDialog { Filter = "Installer entry point|*.exe;*.msi", InitialDirectory = directory, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    public IReadOnlyList<string> Fonts()
    {
        var dialog = new OpenFileDialog { Filter = "Font files|*.ttf;*.otf", Multiselect = true, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }
    public bool Confirm(string message) => MessageBox.Show(message, "NEX Installer", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
