namespace CertCoach.Application.Abstractions;

/// <summary>Stores uploaded files by key. Keys are built from IDs only, never from user input.</summary>
public interface IFileStore
{
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken);

    /// <exception cref="FileNotFoundException">No file is stored under the key.</exception>
    Stream OpenRead(string key);

    /// <summary>Does nothing when the file is already gone.</summary>
    void Delete(string key);
}
