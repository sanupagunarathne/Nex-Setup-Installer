using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Media;
using Microsoft.Win32;
using NexInstaller.Core.Installation;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Importing;
using NexInstaller.Infrastructure.Installation;
namespace NexInstaller.App.Services;

public sealed class WindowsInstallationEnvironment : IInstallationEnvironment
{
    public bool IsSupported => OperatingSystem.IsWindows() && Environment.Is64BitOperatingSystem;
    public bool IsAdministrator { get { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); } }
    public PackageArchitecture Architecture => RuntimeInformation.OSArchitecture switch
    { System.Runtime.InteropServices.Architecture.X64 => PackageArchitecture.X64, System.Runtime.InteropServices.Architecture.Arm64 => PackageArchitecture.Arm64, _ => PackageArchitecture.X86 };
    public string MsiExecPath => Path.Combine(Environment.SystemDirectory, "msiexec.exe");
}
public sealed class WindowsInstallationExecutor : IInstallationExecutor
{
    public async Task<int> ExecuteAsync(InstallationPlan plan)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows is required.");
        if (plan.Request.Kind == InstallationKind.Font)
            return await Task.Run(() =>
            {
                using var identity = WindowsIdentity.GetCurrent();
                using var gate = new Mutex(false, @"Local\NexInstaller.Fonts." + identity.User!.Value);
                var acquired = false;
                try
                {
                    try { acquired = gate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("Another NEX instance is installing fonts.");
                    var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
                    return new UserFontInstaller(directory, new WindowsUserFontApi()).Install(plan);
                }
                finally { if (acquired) gate.ReleaseMutex(); }
            });
        if (!Path.IsPathFullyQualified(plan.FilePath) || !Path.GetExtension(plan.FilePath).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Only a validated absolute EXE entry point or system msiexec is allowed.");
        LibraryStorage.CheckPath(plan.FilePath);
        var start = new ProcessStartInfo { FileName = plan.FilePath, WorkingDirectory = plan.WorkingDirectory, UseShellExecute = true };
        if (plan.RequiresElevation) start.Verb = "runas";
        foreach (var argument in plan.Arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Windows did not provide a process handle. Completion is unknown.");
            await process.WaitForExitAsync(CancellationToken.None);
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new OperationCanceledException("Windows elevation was cancelled.", ex); }
    }
}
public sealed class WindowsUserFontApi : IUserFontApi
{
    private const string RegistryPath = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";
    public string GetRegistrationName(string file)
    {
        var glyph = new GlyphTypeface(new Uri(file, UriKind.Absolute));
        return (glyph.FamilyNames.Values.FirstOrDefault() ?? throw new InvalidDataException("Font family missing.")) + " " +
            (glyph.FaceNames.Values.FirstOrDefault() ?? "Regular") + " (TrueType)";
    }
    public string? GetRegisteredPath(string name) { using var key = Registry.CurrentUser.OpenSubKey(RegistryPath); return key?.GetValue(name) as string; }
    public bool AddResource(string file) => AddFontResourceEx(file, 0, IntPtr.Zero) > 0;
    public void RemoveResource(string file) { RemoveFontResourceEx(file, 0, IntPtr.Zero); }
    public void Register(string name, string file)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true) ?? throw new UnauthorizedAccessException("Current-user font registry is unavailable.");
        if (key.GetValue(name) is not null) throw new IOException("Font registration appeared concurrently; existing value was preserved.");
        key.SetValue(name, file, RegistryValueKind.String);
    }
    public void NotifyChanged() => SendMessageTimeout(new IntPtr(0xffff), 0x001D, IntPtr.Zero, IntPtr.Zero, 0x0002, 1000, out _);
    [DllImport("gdi32.dll", EntryPoint = "AddFontResourceExW", CharSet = CharSet.Unicode)] private static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);
    [DllImport("gdi32.dll", EntryPoint = "RemoveFontResourceExW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveFontResourceEx(string file, uint flags, IntPtr reserved);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
