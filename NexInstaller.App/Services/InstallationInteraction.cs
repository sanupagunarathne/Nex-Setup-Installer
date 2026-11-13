using System.Windows;
using NexInstaller.Core.Installation;
namespace NexInstaller.App.Services;
public sealed class InstallationInteraction : IInstallationInteraction
{
    public Task<bool> ConfirmAsync(InstallationPlan plan) => Application.Current.Dispatcher.InvokeAsync(() =>
    {
        var font = plan.Request.Kind == InstallationKind.Font;
        var text = $"{plan.Name}\n\nID: {plan.Request.Id}\nSource / executable: {plan.FilePath}\nSHA-256: {plan.EntrySha256}\n" +
            (font ? $"Scope: current Windows user ({Environment.UserDomainName}\\{Environment.UserName}) only. Confirm the license permits installation on this device.\n" :
            $"Mode: {(plan.Manual ? "Manual / interactive" : "Verified unattended profile")}\nElevation: {(plan.RequiresElevation ? "Standard Windows UAC prompt expected" : "Current permissions; Windows may request UAC")}\nArguments:\n{string.Join("\n", plan.Arguments)}\n") +
            "\nFiles passed SHA-256 checks. Confirm you trust this package and its license. STAK does not validate publisher signatures or licensing.\n" +
            (!font && plan.Manual ? "Complete the installer manually. Decline any restart option and wait for child installers to finish.\n" : "") +
            "\nStart this item? Stop queue waits for an active installer; closing or killing it can leave a partial installation.";
        return MessageBox.Show(Application.Current.MainWindow, text, "Confirm installation — " + plan.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }).Task;
    public Task<bool> ConfirmManualCompletionAsync(InstallationPlan plan, int exitCode) => Application.Current.Dispatcher.InvokeAsync(() =>
        MessageBox.Show(Application.Current.MainWindow, $"{plan.Name} launcher exited with code {exitCode}.\n\nSome EXE launchers exit before their child installers finish. Wait for all installation windows to finish.\n\nDid installation actually complete successfully? Choose No if it was cancelled or failed. Do not accept a restart now.",
            "Confirm manual installation result", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes).Task;
}
