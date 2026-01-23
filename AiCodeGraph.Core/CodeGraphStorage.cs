namespace AiCodeGraph.Core;

public class CodeGraphStorage : ICodeGraphStorage
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // SQLite initialization will be implemented in a later task
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        // SQLite save will be implemented in a later task
        return Task.CompletedTask;
    }
}
