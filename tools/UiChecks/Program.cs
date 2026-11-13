using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexInstaller.App.ViewModels;
using NexInstaller.App.Views;
using NexInstaller.Core.Configuration;
using NexInstaller.Core.Importing;
using NexInstaller.Core.Logging;
using NexInstaller.Core.Navigation;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Configuration;
using NexInstaller.Infrastructure.Importing;
using NexInstaller.Infrastructure.Library;
using NexInstaller.Infrastructure.Packages;
using NexInstaller.Infrastructure.Presets;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new NexInstaller.App.App(); app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var root = Path.Combine(Path.GetTempPath(), "nex-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "fonts"));
        File.WriteAllText(Path.Combine(root, "fonts", "example.ttf"), "UI fixture; never installed");
        try
        {
            var paths = new LibraryPaths(root, "."); var storage = new LibraryStorage(paths);
            var source = Path.Combine(root, "..", "nex-ui-source-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                File.WriteAllText(source, "UI fixture; never executed");
                Pump(new PackageImportService(storage).ImportAsync(new(source, null, "Example app", "1.0", PackageArchitecture.X64, "Tools")));
            }
            finally { File.Delete(source); }
            var catalog = new FileSystemPackageCatalogService(paths, new JsonPackageManifestReader(), PackageArchitecture.X64);
            using var shell = new ShellViewModel(new NavigationState(), new FileSystemLibrarySnapshotService(paths), new LoadedConfiguration(paths, "test", null), new Log(), catalog,
                fontImports: new FontImportService(storage), presetService: new JsonPresetService(paths, catalog), libraryFolders: new LibraryFolderService(storage), dialogs: new Dialogs());
            Pump(shell.RefreshAsync());
            var fonts = new FontLibraryView { DataContext = shell.FontLibrary, Foreground = (Brush)app.FindResource("TextBrush") };
            Layout(fonts);
            var delete = Descendants(fonts).OfType<Button>().Single(button => Equals(button.Content, "Delete selected font"));
            var bounds = delete.TransformToAncestor(fonts).TransformBounds(new Rect(delete.RenderSize));
            Check(bounds.Top >= 0 && bounds.Bottom < 100, "Font Delete must be visible in the top toolbar.");
            var tools = shell.FontLibrary.FolderTools;
            tools.NewFolderCommand.Execute(null); Layout(fonts);
            Check(Descendants(fonts).OfType<TextBox>().Any(box => box.IsVisible || box.ActualWidth > 0), "Folder name editor must be laid out.");
            tools.NewFolderName = "Client fonts";
            Pump(tools.CreateAsync()); Layout(fonts);
            Check(shell.FontLibrary.Fonts.Count == 1, "Creating a folder must retain the current file list.");
            Check(tools.IsIdle && !shell.InstallationQueue.IsLibraryChanging, "Folder creation must release the busy state.");
            Check(tools.BrowserFolders.Any(folder => folder.Name == "Client fonts"), "Created folder must be visible.");
            Pump(tools.MoveAsync()); Layout(fonts);
            Check(shell.FontLibrary.SelectedFolder.Value == "Client fonts" && shell.FontLibrary.Fonts.Count == 1, "Move must open the destination and retain its font.");
            Check(!Descendants(fonts).OfType<TextBlock>().Any(block => block.Text.Contains("CategoryOption")), "Dropdown must render a label, not an object dump.");
            Save(fonts, "fonts.png");
            var apps = new SoftwareLibraryView { DataContext = shell.SoftwareLibrary, Foreground = (Brush)app.FindResource("TextBrush") };
            shell.SoftwareLibrary.FolderTools!.NewFolderName = "Adobe";
            Pump(shell.SoftwareLibrary.FolderTools.CreateAsync()); Layout(apps); Save(apps, "apps.png");
            Check(apps.DesiredSize.Height <= 600, "App controls must fit the supported viewport or scroll.");
            Check(shell.SoftwareLibrary.Packages.Count == 1, "Creating an app folder must retain the current list.");
            for (var index = 0; index < 3; index++)
            {
                tools.NewFolderName = "Repeated folder " + index;
                Pump(tools.CreateAsync()); Layout(fonts);
                Check(tools.IsIdle && !shell.InstallationQueue.IsLibraryChanging, "Repeated folder creation must release busy state.");
            }
            tools.NewFolderName = "Client fonts";
            try { Pump(tools.CreateAsync()); throw new Exception("Duplicate folder was accepted."); }
            catch (InvalidOperationException exception) when (exception.Message == "Folder already exists.") { }
            Check(tools.IsIdle && !shell.InstallationQueue.IsLibraryChanging, "Failed creation must release busy state.");
            Check(tools.BrowserFolders.Single(folder => folder.Name == "Client fonts").CanDelete, "Named folder must offer Delete.");
            Check(!tools.BrowserFolders[0].CanDelete && !tools.BrowserFolders[1].CanDelete, "Virtual folders must not offer Delete.");
            Pump(tools.DeleteAsync("Client fonts")); Layout(fonts);
            Check(!tools.BrowserFolders.Any(folder => folder.Name == "Client fonts") && shell.FontLibrary.Fonts.Single().Folder == "", "Delete must remove the tile and keep its font Unfiled.");
            Console.WriteLine("WPF checks passed: create, visible folder, move, busy reset, labels, font Delete, layout. Screenshots: artifacts/ui-checks");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Layout(FrameworkElement view)
    {
        view.Width = 1000; view.Height = 600;
        view.Measure(new Size(1000, 600)); view.Arrange(new Rect(0, 0, 1000, 600)); view.UpdateLayout();
        // Let deferred bindings and glyph rendering finish before inspecting or capturing.
        Pump(Task.Delay(60)); view.UpdateLayout();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) => frame.Continue = false;
        task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false));
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Check(task.IsCompleted, "UI operation timed out."); task.GetAwaiter().GetResult();
    }
    private static void Save(FrameworkElement view, string name)
    {
        Directory.CreateDirectory("artifacts/ui-checks");
        var bitmap = new RenderTargetBitmap(1000, 600, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine("artifacts/ui-checks", name)); encoder.Save(output);
    }
    private sealed class Log : ILocalLog
    {
        public string? LogDirectory => null;
        public bool TryWrite(string eventName, string message, Exception? exception = null) => true;
    }
    private sealed class Dialogs : IImportDialogs
    {
        public string? Installer() => null;
        public string? Folder() => null;
        public string? EntryPoint(string directory) => null;
        public IReadOnlyList<string> Fonts() => [];
        public bool Confirm(string message) => true;
    }
}
