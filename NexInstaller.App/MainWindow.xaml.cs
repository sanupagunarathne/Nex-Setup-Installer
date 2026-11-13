using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using NexInstaller.App.ViewModels;

namespace NexInstaller.App;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += (_, args) =>
        {
            if (!viewModel.InstallationQueue.IsBusy) return;
            args.Cancel = true;
            MessageBox.Show(this, "An installation queue is active. Stop the queue and wait for the current installer to finish before closing STAK.", "Installation in progress", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        Loaded += async (_, _) => await viewModel.RefreshAsync();
    }
    private void OpenSocialLink(object sender, RoutedEventArgs e)
    {
        var url = (((Button)sender).Tag as string) switch
        {
            "github" => "https://github.com/sanupagunarathne",
            "linkedin" => "https://www.linkedin.com/in/sanupagunarathne/",
            "instagram" => "https://www.instagram.com/sanuoa.p/",
            "rusith-github" => "https://github.com/Rusiya001",
            "rusith-linkedin" => "https://www.linkedin.com/in/rusith-minol-weerasiri/",
            "rusith-instagram" => "https://www.instagram.com/rusiy._.a/",
            _ => null
        };
        if (url is null) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { MessageBox.Show(this, "Your browser could not open this profile. Internet access is needed for social websites; STAK itself still works offline.", "Open profile", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
}
