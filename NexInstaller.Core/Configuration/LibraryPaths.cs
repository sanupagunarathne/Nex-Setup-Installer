namespace NexInstaller.Core.Configuration;

public sealed class LibraryPaths
{
    public string Root { get; }
    public string Packages => Path.Combine(Root, "packages");
    public string Fonts => Path.Combine(Root, "fonts");
    public string Presets => Path.Combine(Root, "presets");
    public string Manifests => Path.Combine(Root, "manifests");
    public string History => Path.Combine(Root, "history");

    public LibraryPaths(string applicationDirectory, string libraryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        Root = Path.GetFullPath(libraryRoot, Path.GetFullPath(applicationDirectory));
    }
}
