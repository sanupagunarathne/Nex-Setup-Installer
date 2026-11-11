using System.Windows;
using Microsoft.Win32;
using NexInstaller.App.ViewModels;
using NexInstaller.Core.History;
namespace NexInstaller.App.Services;
public sealed class PresetsHistoryDialogs : IPresetsHistoryDialogs
{
    public bool DiscardPresetEdits() => MessageBox.Show(Application.Current.MainWindow, "Discard unsaved preset changes?", "Unsaved preset", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    public bool ReplacePendingQueue() => MessageBox.Show(Application.Current.MainWindow, "Replace the pending queue with this preset? Each installation will still require confirmation.", "Start preset", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    public string? SaveReport(HistoryExportFormat format)
    {
        var dialog = new SaveFileDialog { Filter = format == HistoryExportFormat.Json ? "JSON report|*.json" : "CSV report|*.csv", DefaultExt = format == HistoryExportFormat.Json ? ".json" : ".csv", AddExtension = true, OverwritePrompt = true, FileName = "nex-installation-report" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
