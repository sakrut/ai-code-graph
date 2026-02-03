namespace AiCodeGraph.Core.Query;

public interface IGraphTraversalEngine
{
    Task<TraversalResult> TraverseAsync(TraversalConfig config, CancellationToken ct = default);
}
