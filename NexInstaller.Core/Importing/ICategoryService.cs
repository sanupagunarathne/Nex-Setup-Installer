namespace NexInstaller.Core.Importing;
public interface ICategoryService
{
    IReadOnlyList<string> List();
    void Add(string category);
    void Remove(string category);
    void Assign(string packageId, string category);
}
