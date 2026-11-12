using System.Windows;
using NexInstaller.Core.Presets;
using NexInstaller.Core.History;
using NexInstaller.Infrastructure.Presets;
using NexInstaller.Infrastructure.History;
using NexInstaller.Core.Installation;
using NexInstaller.Infrastructure.Installation;
using NexInstaller.Core.Importing;
using NexInstaller.Infrastructure.Importing;
using NexInstaller.App.Services;
using Microsoft.Extensions.DependencyInjection;
using NexInstaller.App.ViewModels;
using NexInstaller.Core.Library;
using NexInstaller.Core.Packages;
using NexInstaller.Infrastructure.Packages;
using System.Runtime.InteropServices;
using NexInstaller.Core.Logging;
using NexInstaller.Core.Navigation;
using NexInstaller.Infrastructure.Configuration;
using NexInstaller.Infrastructure.Library;
using NexInstaller.Infrastructure.Logging;

namespace NexInstaller.App;

public partial class App : Application
{
    private ServiceProvider? services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var applicationDirectory = AppContext.BaseDirectory;
            var configuration = ConfigurationLoader.Load(applicationDirectory);
            var registrations = new ServiceCollection();
            registrations.AddSingleton(configuration);
            registrations.AddSingleton(configuration.Paths);
            registrations.AddSingleton<NavigationState>();
            registrations.AddSingleton<IPresetService, JsonPresetService>();
            registrations.AddSingleton<IHistoryService>(_ => new JsonHistoryService(applicationDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
            registrations.AddSingleton<IPresetsHistoryDialogs, PresetsHistoryDialogs>();
            registrations.AddSingleton(new VerifiedProfiles(System.IO.Path.Combine(applicationDirectory, "installation-profiles.json")));
            registrations.AddSingleton<IInstallationEnvironment, WindowsInstallationEnvironment>();
            registrations.AddSingleton<IInstallationPreflight, InstallationPreflight>();
            registrations.AddSingleton<IInstallationExecutor, WindowsInstallationExecutor>();
            registrations.AddSingleton<IInstallationInteraction, InstallationInteraction>();
            registrations.AddSingleton<IInstallationJournal>(_ => new JsonInstallationJournal(applicationDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
            registrations.AddSingleton<IInstallationEngine, InstallationEngine>();
            registrations.AddSingleton<LibraryStorage>();
            registrations.AddSingleton<ILibraryRemovalService, LibraryRemovalService>();
            registrations.AddSingleton<IPackageImportService, PackageImportService>();
            registrations.AddSingleton<IFontImportService, FontImportService>();
            registrations.AddSingleton<ICategoryService, CategoryService>();
            registrations.AddSingleton<IImportDialogs, ImportDialogs>();
            registrations.AddSingleton<IPackageManifestReader, JsonPackageManifestReader>();
            registrations.AddSingleton<IPackageCatalogService>(provider => new FileSystemPackageCatalogService(
                configuration.Paths, provider.GetRequiredService<IPackageManifestReader>(),
                RuntimeInformation.OSArchitecture switch
                {
                    Architecture.X64 => PackageArchitecture.X64,
                    Architecture.X86 => PackageArchitecture.X86,
                    Architecture.Arm64 => PackageArchitecture.Arm64,
                    _ => throw new PlatformNotSupportedException("Unsupported operating system architecture.")
                }));
            registrations.AddSingleton<ILibrarySnapshotService, FileSystemLibrarySnapshotService>();
            registrations.AddSingleton<ILocalLog>(_ => new JsonLocalLog(applicationDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
            registrations.AddSingleton<ShellViewModel>();
            registrations.AddSingleton<MainWindow>();
            services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            services.GetRequiredService<ILocalLog>().TryWrite("application.start", "NEX Installer foundation started.");
            MainWindow = services.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            services?.GetService<ILocalLog>()?.TryWrite("application.start.failed", "Application startup failed.", ex);
            MessageBox.Show("NEX Installer could not start. Check the local configuration and permissions.",
                "NEX Installer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        services?.GetService<ILocalLog>()?.TryWrite("application.exit", "NEX Installer closed.");
        services?.Dispose();
        base.OnExit(e);
    }
}
