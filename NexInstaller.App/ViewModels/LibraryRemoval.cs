using NexInstaller.Core.Importing;
using NexInstaller.Core.Installation;

namespace NexInstaller.App.ViewModels;

public sealed partial class ShellViewModel
{
    private readonly ILibraryRemovalService? removal;
    private readonly IImportDialogs? removalDialogs;

    private void CheckRemovalAvailable()
    {
        if (InstallationQueue.IsBusy || InstallationQueue.IsLibraryChanging || PackageManager.IsBusy || FontLibrary.IsBusy || Presets.IsBusy)
            throw new InvalidOperationException("Wait for the current installation or library operation to finish before deleting files.");
        if (removal is null || removalDialogs is null) throw new InvalidOperationException("Library deletion is unavailable.");
    }

    public async Task DeleteSelectedAppsAsync()
    {
        CheckRemovalAvailable();
        var rows = SoftwareLibrary.SelectedPackages.Count > 0 ? SoftwareLibrary.SelectedPackages
            : SoftwareLibrary.SelectedPackage is { } inspected ? [inspected] : Array.Empty<PackageRowViewModel>();
        if (rows.Count == 0) { SoftwareLibrary.ManagementStatus = "Select an app to delete first."; return; }
        var ids = rows.Select(row => row.Package.Manifest.Id).ToArray();
        if (!removalDialogs!.Confirm($"Delete {ids.Length} selected app(s) from the saved library?\n\n{string.Join("\n", rows.Select(row => row.Name))}\n\nThey will be removed from the pending install list. Saved setups referencing them will need updating. Copies are kept in the library's .trash folder; source files and installed apps are unchanged."))
        { SoftwareLibrary.ManagementStatus = "Deletion cancelled."; return; }
        InstallationQueue.IsLibraryChanging = true;
        try
        {
            await removal!.RemovePackagesAsync(ids);
            foreach (var item in InstallationQueue.Items.Where(item => item.Request.Kind == InstallationKind.Software && ids.Contains(item.Request.Id, StringComparer.OrdinalIgnoreCase)).ToArray()) item.RemoveCommand.Execute(null);
            await RefreshAsync();
            SoftwareLibrary.ManagementStatus = $"Deleted {ids.Length} app(s) from the library. Recoverable copies are in .trash.";
            RecordOperation("package.remove", $"Removed {ids.Length} saved apps.");
        }
        finally { InstallationQueue.IsLibraryChanging = false; }
    }

    public async Task DeleteSelectedFontAsync()
    {
        CheckRemovalAvailable();
        var font = FontLibrary.SelectedFont;
        if (font is null) { FontLibrary.ReportRemoval("Select a font to delete first."); return; }
        if (!removalDialogs!.Confirm($"Delete '{font.Name}' from the saved font library? It will be removed from the pending install list. Saved setups referencing it will need updating. A copy is kept in .trash. This does not uninstall the font from Windows."))
        { FontLibrary.ReportRemoval("Deletion cancelled."); return; }
        InstallationQueue.IsLibraryChanging = true;
        try
        {
            await removal!.RemoveFontAsync(font.Path);
            foreach (var item in InstallationQueue.Items.Where(item => item.Request.Kind == InstallationKind.Font && item.Request.Id.Equals("font-" + font.Sha256, StringComparison.OrdinalIgnoreCase)).ToArray()) item.RemoveCommand.Execute(null);
            await RefreshAsync();
            FontLibrary.ReportRemoval("Font deleted from the library. A recoverable copy is in .trash.");
            RecordOperation("font.remove", "Removed a saved font.");
        }
        finally { InstallationQueue.IsLibraryChanging = false; }
    }
}
