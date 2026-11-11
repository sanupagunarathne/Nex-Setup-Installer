using System.Windows;
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
            MessageBox.Show(this, "An installation queue is active. Stop the queue and wait for the current installer to finish before closing NEX.", "Installation in progress", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        Loaded += async (_, _) => await viewModel.RefreshAsync();
    }
}
