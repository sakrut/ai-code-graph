namespace AiCodeGraph.Core;

public interface ICodeGraphStorage
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
}
