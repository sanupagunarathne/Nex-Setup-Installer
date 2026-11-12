using System.Text.RegularExpressions;

namespace NexInstaller.Core.Importing;

public static class LibraryFolder
{
    public static string Normalize(string? name)
    {
        var value = name?.Trim() ?? "";
        if (value.Length == 0) return "";
        if (value.Length > 80 || value is "." or ".." || value.EndsWith('.') ||
            value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) ||
            Regex.IsMatch(value.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("Use a folder name of up to 80 characters without slashes, reserved Windows names, or a trailing dot.");
        return value;
    }
}
