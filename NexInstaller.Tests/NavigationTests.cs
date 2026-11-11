using NexInstaller.Core.Navigation;

namespace NexInstaller.Tests;

public sealed class NavigationTests
{
    [Fact]
    public void StartsOnDashboard() => Assert.Equal(PageId.Dashboard, new NavigationState().CurrentPage);

    [Theory]
    [InlineData(PageId.SoftwareLibrary)]
    [InlineData(PageId.FontLibrary)]
    [InlineData(PageId.Presets)]
    [InlineData(PageId.PackageManager)]
    [InlineData(PageId.History)]
    [InlineData(PageId.Settings)]
    public void NavigationNotifiesOnlyOnChange(PageId page)
    {
        var navigation = new NavigationState();
        var notifications = new List<string?>();
        navigation.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        navigation.Navigate(page);
        navigation.Navigate(page);
        Assert.Equal(page, navigation.CurrentPage);
        Assert.Equal([nameof(NavigationState.CurrentPage)], notifications);
    }

    [Fact]
    public void RejectsUndefinedPageWithoutChangingState()
    {
        var navigation = new NavigationState();
        Assert.Throws<ArgumentOutOfRangeException>(() => navigation.Navigate((PageId)999));
        Assert.Equal(PageId.Dashboard, navigation.CurrentPage);
    }
}
