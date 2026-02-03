namespace AiCodeGraph.Core.Query;

public record TraversalNodeMetrics(
    int CognitiveComplexity = 0,
    int LinesOfCode = 0,
    int AfferentCoupling = 0,
    int EfferentCoupling = 0);

public record TraversalNode(
    string MethodId,
    string FullName,
    int Depth,
    TraversalDirection Direction,
    float RankingScore = 0,
    TraversalNodeMetrics? Metrics = null)
{
    public float RankingScore { get; set; } = RankingScore;
    public TraversalNodeMetrics? Metrics { get; set; } = Metrics;
}

public record TraversalEdge(
    string FromMethodId,
    string ToMethodId,
    TraversalDirection EdgeDirection);

public record TraversalResult(
    string SeedMethodId,
    string SeedMethodFullName,
    IReadOnlyList<TraversalNode> Nodes,
    IReadOnlyList<TraversalEdge> Edges,
    int TotalNodesVisited,
    long TraversalTimeMs,
    TraversalConfig Config)
{
    public static TraversalResult Empty(string seedMethodId, TraversalConfig config) =>
        new(seedMethodId, seedMethodId, Array.Empty<TraversalNode>(), Array.Empty<TraversalEdge>(), 0, 0, config);
}
