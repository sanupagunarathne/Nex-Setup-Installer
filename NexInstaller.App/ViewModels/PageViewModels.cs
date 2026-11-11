namespace NexInstaller.App.ViewModels;

public abstract class PageViewModel(string title, string subtitle) : ObservableObject
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
}

public sealed class DashboardViewModel(ShellViewModel shell) : PageViewModel("Dashboard", "Your offline installation workspace.")
{
    public ShellViewModel Shell { get; } = shell;
}

public sealed class SettingsViewModel(ShellViewModel shell) : PageViewModel("Settings", "Local paths, diagnostics, and application information.")
{
    public ShellViewModel Shell { get; } = shell;
}
