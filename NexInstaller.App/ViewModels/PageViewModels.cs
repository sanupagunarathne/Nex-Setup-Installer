namespace NexInstaller.App.ViewModels;

public abstract class PageViewModel(string title, string subtitle)
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
}

public sealed class DashboardViewModel(ShellViewModel shell) : PageViewModel("Dashboard", "Your offline installation workspace.")
{
    public ShellViewModel Shell { get; } = shell;
}

public sealed class SoftwareLibraryViewModel : PageViewModel
{
    public SoftwareLibraryViewModel() : base("Software Library", "Browse your local software collection and prepare an installation queue.") { }
}

public sealed class FontLibraryViewModel : PageViewModel
{
    public FontLibraryViewModel() : base("Font Library", "Keep licensed typefaces ready for every workstation.") { }
}

public sealed class PresetsViewModel : PageViewModel
{
    public PresetsViewModel() : base("Installation Presets", "Bring your most common workstation setups together.") { }
}

public sealed class PackageManagerViewModel : PageViewModel
{
    public PackageManagerViewModel() : base("Package Manager", "Maintain the packages in your portable library.") { }
}

public sealed class HistoryViewModel : PageViewModel
{
    public HistoryViewModel() : base("Installation History", "Review local installation sessions and their outcomes.") { }
}

public sealed class SettingsViewModel(ShellViewModel shell) : PageViewModel("Settings", "Local paths, diagnostics, and application information.")
{
    public ShellViewModel Shell { get; } = shell;
}
