using NexInstaller.Core.Installation;
using NexInstaller.Infrastructure.Importing;
namespace NexInstaller.Infrastructure.Installation;

public interface IUserFontApi
{
    string GetRegistrationName(string file);
    string? GetRegisteredPath(string name);
    bool AddResource(string file);
    void RemoveResource(string file);
    void Register(string name, string file);
    void NotifyChanged();
}
/// <summary>Caller holds the per-user font mutation lock. Never replaces another registered face.</summary>
public sealed class UserFontInstaller(string destinationDirectory, IUserFontApi api)
{
    public int Install(InstallationPlan plan)
    {
        if (plan.Request.Kind != InstallationKind.Font || string.IsNullOrEmpty(plan.EntrySha256)) throw new InvalidDataException("A preflight-verified font plan is required.");
        LibraryStorage.CheckPath(plan.FilePath); LibraryStorage.CheckPath(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.Combine(destinationDirectory, plan.EntrySha256.ToLowerInvariant() + Path.GetExtension(plan.FilePath).ToLowerInvariant());
        LibraryStorage.CheckPath(destination);
        var temporary = Path.Combine(destinationDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        var created = false; var resourceAdded = false; var registered = false;
        try
        {
            File.Copy(plan.FilePath, temporary, false);
            if (!LibraryStorage.Hash(temporary, default).Equals(plan.EntrySha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Font changed while copying.");
            var name = api.GetRegistrationName(plan.FilePath);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 512 || name.Any(char.IsControl)) throw new InvalidDataException("Invalid font registration name.");
            var existing = api.GetRegisteredPath(name);
            if (existing is not null)
            {
                if (existing.Equals(destination, StringComparison.OrdinalIgnoreCase) && File.Exists(destination) && LibraryStorage.Hash(destination, default).Equals(plan.EntrySha256, StringComparison.OrdinalIgnoreCase)) return 0;
                throw new IOException("A different font already uses this face name. It was not overwritten.");
            }
            if (File.Exists(destination))
            {
                if (!LibraryStorage.Hash(destination, default).Equals(plan.EntrySha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Destination font conflict.");
            }
            else { File.Move(temporary, destination); created = true; }
            if (!api.AddResource(destination)) throw new IOException("Windows rejected the font resource.");
            resourceAdded = true;
            api.Register(name, destination); registered = true;
            // Notification failure does not undo a successful persistent registration.
            try { api.NotifyChanged(); } catch { }
            return 0;
        }
        finally
        {
            if (!registered && resourceAdded) api.RemoveResource(destination);
            if (!registered && created && File.Exists(destination)) File.Delete(destination);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
