using CertCoach.Application.Abstractions;

namespace CertCoach.Infrastructure.Storage;

/// <summary>Files on local disk, a Docker volume in production. Writes go to a temporary name first so a crash never leaves half a file.</summary>
internal sealed class LocalFileStore(string root) : IFileStore
{
    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporary = path + ".partial";
        await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        File.Move(temporary, path, overwrite: true);
    }

    public Stream OpenRead(string key) =>
        new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    public void Delete(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Keys come from IDs, but the check keeps any key from escaping the root.</summary>
    private string PathFor(string key)
    {
        var path = Path.GetFullPath(Path.Combine(root, key));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("The storage key points outside the storage root.", nameof(key));
        }

        return path;
    }
}
