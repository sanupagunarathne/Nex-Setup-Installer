using NexInstaller.Core.Navigation;

namespace NexInstaller.App.ViewModels;

public sealed class NavigationItemViewModel(PageId id, string title, string glyph, Action<PageId> navigate) : ObservableObject
{
    private bool active;
    public PageId Id { get; } = id;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public RelayCommand SelectCommand { get; } = new(() => navigate(id));
    public bool IsActive
    {
        get => active;
        set { if (active == value) return; active = value; Raise(); }
    }
}
