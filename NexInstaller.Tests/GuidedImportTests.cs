using NexInstaller.App.ViewModels;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.Logging;
using NexInstaller.Core.Navigation;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Configuration;
using NexInstaller.Infrastructure.Importing;
using NexInstaller.Infrastructure.Library;
using NexInstaller.Infrastructure.Packages;
namespace NexInstaller.Tests;
public sealed class GuidedImportTests
{
    private static ShellViewModel Shell(string root)
    {
        var paths = new LibraryPaths(root, ".");
        return new(new NavigationState(), new FileSystemLibrarySnapshotService(paths), new LoadedConfiguration(paths, "nexinstaller.json", null), new Log(),
            new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64), categories: new CategoryService(new LibraryStorage(paths)));
    }
    [Fact]
    public void BuiltInGroupsAreAvailableAndCreateOptionDoesNotBecomeMetadata()
    {
        using var directory = new TemporaryDirectory(); using var shell = Shell(directory.Root); var vm = shell.PackageManager;
        Assert.Contains(vm.ImportGroups, group => group.Value == "Office"); Assert.Contains(vm.ImportGroups, group => group.Value == "Browsers");
        var original = vm.Category; vm.SelectedImportGroup = vm.ImportGroups.Last();
        Assert.True(vm.IsCreatingGroup); Assert.Equal(original, vm.Category); Assert.True(vm.SelectedImportGroup!.IsCreate);
        vm.CancelCreateGroupCommand.Execute(null); Assert.False(vm.IsCreatingGroup); Assert.False(vm.SelectedImportGroup!.IsCreate);
        Assert.Empty(Directory.GetFiles(directory.Root));
    }
    [Fact]
    public async Task CustomGroupIsPersistedSelectedAndKeptAboveCreateOption()
    {
        using var directory = new TemporaryDirectory(); using var shell = Shell(directory.Root); var vm = shell.PackageManager;
        vm.NewCategory = "Shop tools"; await vm.CreateImportGroupAsync();
        Assert.Equal("Shop tools", vm.Category); Assert.True(vm.ImportGroups.Last().IsCreate);
        Assert.Contains("Shop tools", new CategoryService(new LibraryStorage(new LibraryPaths(directory.Root, "."))).List());
        Assert.Contains(vm.ImportGroups, group => group.Value == "Shop tools");
    }
    [Fact]
    public async Task RefreshKeepsExistingGroupsWithoutDuplicatingBuiltIns()
    {
        using var directory = new TemporaryDirectory(); var service = new CategoryService(new LibraryStorage(new LibraryPaths(directory.Root, ".")));
        service.Add("office"); service.Add("Legacy group"); using var shell = Shell(directory.Root); await shell.RefreshAsync();
        Assert.Single(shell.PackageManager.ImportGroups, group => string.Equals(group.Value, "office", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(shell.PackageManager.ImportGroups, group => group.Value == "Legacy group"); Assert.True(shell.PackageManager.ImportGroups.Last().IsCreate);
    }
    [Fact]
    public void ReviewAndBackKeepAllAdvancedMetadata()
    {
        using var root = new TemporaryDirectory(); using var source = new TemporaryDirectory(); using var shell = Shell(root.Root); var vm = shell.PackageManager;
        vm.SourcePath = source.Write("setup.exe", "dummy"); vm.NextCommand.Execute(null); Assert.Equal(1, vm.Step);
        vm.PackageName = "Editor"; vm.Version = "1.0"; vm.Category = "Office"; vm.Architecture = PackageArchitecture.X86; vm.RequiresAdministrator = true; vm.Description = "Staff note";
        vm.NextCommand.Execute(null); Assert.Equal(2, vm.Step); Assert.Contains("X86", vm.ReviewText); Assert.Contains("Staff note", vm.ReviewText);
        vm.BackCommand.Execute(null); Assert.Equal(PackageArchitecture.X86, vm.Architecture); Assert.True(vm.RequiresAdministrator); Assert.Equal("Office", vm.Category);
        Assert.Empty(Directory.GetFiles(root.Root));
    }
    [Fact]
    public void MissingRequiredDetailsCannotAdvanceToReview()
    {
        using var root = new TemporaryDirectory(); using var source = new TemporaryDirectory(); using var shell = Shell(root.Root); var vm = shell.PackageManager;
        vm.SourcePath = source.Write("setup.msi", "dummy"); vm.NextCommand.Execute(null); vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.Step); Assert.Contains("app name", vm.Status);
    }
    [Fact]
    public void UnfinishedGroupCreationCannotBeSavedAsAGroupName()
    {
        using var root = new TemporaryDirectory(); using var source = new TemporaryDirectory(); using var shell = Shell(root.Root); var vm = shell.PackageManager;
        vm.SourcePath = source.Write("setup.exe", "dummy"); vm.NextCommand.Execute(null); vm.PackageName = "App"; vm.Version = "1.0";
        vm.SelectedImportGroup = vm.ImportGroups.Last(); vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.Step); Assert.DoesNotContain("Create new", vm.Category); Assert.Contains("cancel", vm.Status);
    }
    [Fact]
    public void FolderNeedsAnEntryPointInsideItsOwnFolder()
    {
        using var root = new TemporaryDirectory(); using var source = new TemporaryDirectory(); using var shell = Shell(root.Root); var vm = shell.PackageManager;
        source.Write("setup.exe", "dummy"); vm.SourcePath = source.Root; vm.EntryPoint = "../setup.exe"; vm.NextCommand.Execute(null); Assert.Equal(0, vm.Step);
        vm.EntryPoint = "setup.exe"; vm.NextCommand.Execute(null); Assert.Equal(1, vm.Step);
    }
    private sealed class Log : ILocalLog
    {
        public string? LogDirectory => null;
        public bool TryWrite(string eventName, string message, Exception? exception = null) => true;
    }
}
