using AiCodeGraph.Core.Storage;

namespace AiCodeGraph.Core;

public class CodeGraphStorage : ICodeGraphStorage
{
    private readonly StorageService _storage;

    public CodeGraphStorage(string? dbPath = null)
    {
        _storage = new StorageService(dbPath);
    }

    public StorageService Storage => _storage;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _storage.InitializeAsync(cancellationToken);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        // No-op for now; explicit save methods on StorageService are used directly
        await Task.CompletedTask;
    }
}
