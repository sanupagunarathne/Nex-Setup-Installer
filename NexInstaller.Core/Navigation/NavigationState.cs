using System.ComponentModel;

namespace NexInstaller.Core.Navigation;

public sealed class NavigationState : INotifyPropertyChanged
{
    public PageId CurrentPage { get; private set; } = PageId.Dashboard;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Navigate(PageId page)
    {
        if (!Enum.IsDefined(page))
            throw new ArgumentOutOfRangeException(nameof(page));
        if (CurrentPage == page)
            return;

        CurrentPage = page;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPage)));
    }
}
