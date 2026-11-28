using Microsoft.Extensions.Options;
using SwfCrypt.Api.Config;

namespace SwfCrypt.Api.Services;

public sealed class SwfFileStore(IOptions<StorageOptions> options)
{
    private string Root => Path.GetFullPath(options.Value.SwfFolder);

    public async Task<string> SaveAsync(Guid id, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var name = $"{id:N}.swf";
        await File.WriteAllBytesAsync(Path.Combine(Root, name), bytes, ct);
        return name;
    }

    public Task<byte[]> ReadAsync(string storageKey, CancellationToken ct) =>
        File.ReadAllBytesAsync(Resolve(storageKey), ct);

    public void Delete(string storageKey)
    {
        try
        {
            File.Delete(Resolve(storageKey));
        }
        catch (Exception)
        {
            // Best effort cleanup.
        }
    }

    private string Resolve(string storageKey)
    {
        var full = Path.GetFullPath(Path.Combine(Root, storageKey));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid storage path.");
        }

        return full;
    }
}
