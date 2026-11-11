using System.Text.RegularExpressions;

namespace NexInstaller.Infrastructure.Packages;

/// <summary>Portable lexical validation; filesystem links are checked separately during discovery.</summary>
public static class PortablePackagePath
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (segments.Length < 3 || segments[0] != "packages")
            throw new ArgumentException("Package files must be beneath packages/<package-folder>/.");
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
                segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.Any(c => c < 32 || "<>:\"|?*".Contains(c)) ||
                Regex.IsMatch(segment.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new ArgumentException("Package path contains an unsafe or Windows-reserved component.");
        }
        return normalized;
    }

    public static string Resolve(string root, string relativePath)
    {
        var normalized = Normalize(relativePath);
        var fullRoot = System.IO.Path.GetFullPath(root);
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, normalized.Replace('/', System.IO.Path.DirectorySeparatorChar)));
    }
}
