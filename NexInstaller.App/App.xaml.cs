using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NexInstaller.App.ViewModels;
using NexInstaller.Core.Library;
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
