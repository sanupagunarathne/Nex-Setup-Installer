using NexInstaller.Core.Configuration;
using NexInstaller.Infrastructure.Importing;
namespace NexInstaller.Tests;
public sealed class CategoryTests
{
    [Fact]
    public async Task CreatesAssignsAndRemovesOnlyUnusedCategories()
    {
        using var library = new TemporaryDirectory(); using var source = new TemporaryDirectory();
        var storage = new LibraryStorage(new LibraryPaths(library.Root, "."));
        var categories = new CategoryService(storage);
        categories.Add("Office");
        Assert.Throws<InvalidOperationException>(() => categories.Add("office"));
        var package = await new PackageImportService(storage).ImportAsync(PackageImportTests.Request(source.Write("setup.exe", "test")));
        categories.Assign(package.Manifest!.Id, "Office");
        Assert.Throws<InvalidOperationException>(() => categories.Remove("Office"));
        categories.Add("Other"); categories.Assign(package.Manifest.Id, "Other"); categories.Remove("Office");
        Assert.DoesNotContain("Office", categories.List());
        Assert.Equal("Other", Assert.Single(storage.ReadManifests()).Manifest.Category);
    }
    [Fact]
    public void InvalidSavedCategoriesAreNotOverwritten()
    {
        using var library = new TemporaryDirectory();
        var path = library.Write("categories.json", "{");
        var categories = new CategoryService(new LibraryStorage(new LibraryPaths(library.Root, ".")));
        Assert.Throws<System.Text.Json.JsonException>(() => categories.Add("Office"));
        Assert.Equal("{", File.ReadAllText(path));
    }
    [Theory]
    [InlineData("")][InlineData(" bad ")][InlineData("a\nb")]
    public void RejectsInvalidCategoryWithoutWriting(string value)
    {
        using var library = new TemporaryDirectory();
        Assert.Throws<ArgumentException>(() => new CategoryService(new LibraryStorage(new LibraryPaths(library.Root, "."))).Add(value));
        Assert.Empty(Directory.GetFiles(library.Root));
    }
}
